using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StackExchange.NetGain.WebSockets
{
    public abstract class WebSocketsProcessor : IProtocolProcessor
    {

        int IProtocolProcessor.ProcessIncoming(NetContext context, Connection connection,
                                               System.IO.Stream incomingBuffer)
        {
            return ProcessIncoming(context, connection, incomingBuffer);
        }
        void IProtocolProcessor.InitializeInbound(NetContext context, Connection connection)
        {
            InitializeInbound(context, connection);
        }
        void IProtocolProcessor.InitializeOutbound(NetContext context, Connection connection)
        {
            InitializeOutbound(context, connection);
        }
        void IProtocolProcessor.GracefulShutdown(NetContext context, Connection connection)
        {
            GracefulShutdown(context, connection);
        }
        protected virtual void InitializeInbound(NetContext context, Connection connection) { }
        protected virtual void InitializeOutbound(NetContext context, Connection connection) { }
        protected abstract void GracefulShutdown(NetContext context, Connection connection);
        protected abstract int ProcessIncoming(NetContext context, Connection connection,
                                               System.IO.Stream incomingBuffer);

        public abstract void Send(NetContext context, Connection connection, object message);

        private object dataQueue, controlQueue;
        IFrame IProtocolProcessor.GetOutboundFrame(NetContext context)
        {            
            lock(this)
            {
                var result = ProtocolProcessor.GetFrame(context, ref controlQueue);
                if(result != null)
                {
#if VERBOSE
                    Debug.WriteLine("popped (control): " + result);   
#endif
                }
                else
                {
                    result = ProtocolProcessor.GetFrame(context, ref dataQueue);
                    if (result != null)
                    {
#if VERBOSE
                        Debug.WriteLine("popped (data): " + result);
#endif
                    }
                }
                return result;
            }            
        }

        protected void SendData(NetContext context, IFrame frame)
        {
            lock(this)
            {
                ProtocolProcessor.AddFrame(context, ref dataQueue, frame);
#if VERBOSE
                Debug.WriteLine("pushed (data): " + frame);
#endif
            }
        }
        protected void SendData(NetContext context, IList<IFrame> batch)
        {
            lock (this)
            {
                foreach (var frame in batch)
                {
                    ProtocolProcessor.AddFrame(context, ref dataQueue, frame);
#if VERBOSE
                    Debug.WriteLine("pushed (data): " + frame);
#endif
                }
            }
        }
        protected void SendShutdown(NetContext context)
        {
            SendControl(context, ShutdownFrame.Default);
        }
        protected void SendControl(NetContext context, IFrame frame)
        {
            lock (this)
            {
                ProtocolProcessor.AddFrame(context, ref controlQueue, frame);
#if VERBOSE
                Debug.WriteLine("pushed (control): " + frame);
#endif
            }
        }
        void IProtocolProcessor.InitializeClientHandshake(NetContext context, Connection connection)
        {
            InitializeClientHandshake(context, connection);
        }

        protected abstract void InitializeClientHandshake(NetContext context, Connection connection);

        protected internal abstract void CompleteHandshake(NetContext context, Connection connection,
                                                          System.IO.Stream input, string requestLine,
                                                          StringDictionary headers, byte[] body);

        protected internal static int ReadInt32(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }

        private Stream incoming;
        protected void Buffer(NetContext context, byte[] buffer, int offset, int count)
        {
            if (incoming == null) incoming = new BufferStream(context, context.Handler.MaxIncomingQuota);
            incoming.Position = incoming.Length;
            incoming.Write(buffer, offset, count);
        }


        private object holder;
        protected void Push(NetContext context, WebSocketsFrame frame)
        {
            lock (this)
            {
                int maxQuota = context.Handler.MaxIncomingQuota;
                if (maxQuota > 0 && ProtocolProcessor.Sum(holder, f =>
                {
                    var typed = f as WebSocketsFrame;
                    return typed == null ? 0 : typed.GetLengthEstimate();
                }) + frame.GetLengthEstimate() > maxQuota)
                {
                    throw new InvalidOperationException("Inbound quota exceeded");
                }
                ProtocolProcessor.AddFrame(context, ref holder, frame);
            }
        }
        protected IFrame Pop(NetContext context)
        {
            lock (this)
            {
                return ProtocolProcessor.GetFrame(context, ref holder);
            }
        }
        protected int FrameCount
        {
            get
            {
                lock (this)
                {
                    return ProtocolProcessor.GetFrameCount(holder);
                }
            }
        }
        protected void ProcessBinary(NetContext context, Connection connection)
        {
            if (incoming == null) return;
            byte[] value;
            incoming.Position = 0;
            using (incoming)
            {
                value = new byte[incoming.Length];
                NetContext.Fill(incoming, value, (int)incoming.Length);
            }
            incoming = null;
            context.Handler.OnReceived(connection, value);
        }
        protected void ProcessText(NetContext context, Connection connection, Encoding encoding = null)
        {
            if (incoming == null) return;
            string value;
            incoming.Position = 0;
            using(incoming)
            using(var sr = new StreamReader(incoming, encoding ?? Encoding.UTF8))
            {
                 value = sr.ReadToEnd();
            }
            incoming = null;
            context.Handler.OnReceived(connection, value);
        }
    }
}
