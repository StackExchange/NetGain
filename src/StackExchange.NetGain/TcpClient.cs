
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.NetGain
{
    public class TcpClient : TcpHandler
    {
        public TcpClient()
        {
            WaitTimeout = 1000;
        }
        public int WaitTimeout { get; set; }

        internal static object WaitImpl(Task<object> task, int timeout)
        {
            if (task.IsFaulted)
            {
                Exception ex = task.Exception;
                var aex = ex as AggregateException;
                if (aex != null && aex.InnerExceptions.Count == 1)
                {
                    ex = aex.InnerExceptions[0];
                }
                throw ex;
            }
            if (!task.IsCompleted)
            {
                try
                {
                    if (!task.Wait(timeout)) throw new TimeoutException();
                }
                catch (AggregateException aex)
                {
                    if (aex.InnerExceptions.Count == 1) throw aex.InnerExceptions[0];
                    throw;
                }
            }
            return task.Result;
        }
        public object Wait(Task<object> task, int timeout = -1)
        {
            return WaitImpl(task, timeout < 0 ? WaitTimeout : timeout);
        }
        public override string ToString()
        {
            return "client";
        }
        private ClientNode node;
        
        public void Open(IPEndPoint endpoint, Action<Connection> connectionInitializer = null)
        {
            if (endpoint == null) throw new InvalidOperationException("No EndPoint specified");

            if (node != null) throw new InvalidOperationException("Already opened");

            var tmp = new ClientNode(this, endpoint);
            try
            {
                Exception ex;
                if ((ex = tmp.TryOpen(connectionInitializer)) != null)
                {
                    throw ex;
                }
                else if (Interlocked.CompareExchange(ref node, tmp, null) == null)
                {
                   tmp = null;
                }
            } finally
            {
                if (tmp != null) tmp.Close();
            }
        }
        public void Dispose()
        {
            Close();
        }

        public Task<object> Execute(object message, bool expectReply = true, object state = null)
        {
            return node.Execute(message, expectReply, state);
        }
        public object ExecuteSync(object message, int timeout = -1)
        {
            var task = Execute(message, true, null);
            return Wait(task, timeout);
        }
    }
}
