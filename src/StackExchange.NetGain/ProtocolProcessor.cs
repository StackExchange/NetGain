
using System;
using System.Diagnostics;

namespace StackExchange.NetGain
{
    public abstract class ProtocolProcessor : IProtocolProcessor
    {

        private object singleFrameOrList;

        int IProtocolProcessor.ProcessIncoming(NetContext context, Connection connection,
                                               System.IO.Stream incomingBuffer)
        {
            return ProcessIncoming(context, connection, incomingBuffer);
        }

        void IProtocolProcessor.GracefulShutdown(NetContext context, Connection connection)
        {
            GracefulShutdown(context, connection);
        }
        void IProtocolProcessor.InitializeInbound(NetContext context, Connection connection)
        {
            InitializeInbound(context, connection);
        }
        void IProtocolProcessor.InitializeOutbound(NetContext context, Connection connection)
        {
            InitializeOutbound(context, connection);
        }

        protected virtual void GracefulShutdown(NetContext context, Connection connection)
        {
            EnqueueFrame(context, ShutdownFrame.Default);
            connection.PromptToSend(context);
        }
        protected virtual void InitializeInbound(NetContext context, Connection connection) { }
        protected virtual void InitializeOutbound(NetContext context, Connection connection) { }
        protected abstract int ProcessIncoming(NetContext context, Connection connection, System.IO.Stream incomingBuffer);
        protected abstract void Send(NetContext context, Connection connection, object message);

        protected virtual void InitializeClientHandshake(NetContext context, Connection connection)
        {
            /* nothing to do */
        }

        void IProtocolProcessor.InitializeClientHandshake(NetContext context, Connection connection)
        {
            InitializeClientHandshake(context, connection);
        }
        protected void EnqueueFrame(NetContext context, IFrame frame)
        {
            lock(this)
            {
                AddFrame(context, ref singleFrameOrList, frame);
            }
        }
        internal static void AddFrame(NetContext context, ref object holder, IFrame frame)
        {
            if (holder == null)
            {
                holder = frame;
            }
            else
            {
                var list = holder as CircularBuffer<IFrame>;
                if (list == null)
                {
                    list = context.GetFrameBuffer();
                    list.Push((IFrame)holder);
                    list.Push(frame);
                    holder = list;
                }
                else
                {
                    list.Push(frame);
                }
            }
        }
        internal static int GetFrameCount(object holder)
        {
            if (holder == null) return 0;
            var frame = holder as IFrame;
            if (frame != null) return 1;
            var list = (CircularBuffer<IFrame>)holder;
            return list.Count;
        }
        internal static IFrame GetFrame(NetContext context, ref object holder)
        {
            if (holder == null) return null;
            var frame = holder as IFrame;
            if (frame != null)
            {
                holder = null;
                return frame;
            }

            var list = (CircularBuffer<IFrame>)holder;
            switch(list.Count)
            {
                case 0:
                    holder = null;
                    context.Recycle(list);
                    return null;
                case 1:
                    frame = list.Pop();
                    holder = null;
                    context.Recycle(list);
                    return frame;
                case 2:
                    frame = list.Pop();
                    holder = list.Pop();
                    context.Recycle(list);
                    return frame;
                default:
                    return list.Pop();
            }
        }
        void IProtocolProcessor.Send(NetContext context, Connection connection, object message)
        {
            Send(context, connection, message);
        }

        IFrame IProtocolProcessor.GetOutboundFrame(NetContext context)
        {
            lock(this)
            {
                return GetFrame(context, ref singleFrameOrList);
            }
        }



        internal static int Sum(object holder, Func<IFrame, int> selector)
        {
            if (holder == null) return 0;
            var frame = holder as IFrame;
            if (frame != null) return selector(frame);

            var list = (CircularBuffer<IFrame>) holder;
            int sum = 0;
            for(int i = 0 ; i < list.Count ; i++)
            {
                sum += selector(list[i]);
            }
            return sum;
        }
    }
}
