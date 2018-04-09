using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace StackExchange.NetGain
{
    partial class TcpHandler
    {
        public object[] Extensions {
            get { return context.Extensions; }
            set { context.Extensions = value;}
        }
        protected class ClientNode
        {
            private readonly Queue<TaskCompletionSource<object>> incoming = new Queue<TaskCompletionSource<object>>();
            private readonly Queue<Tuple<object, bool, TaskCompletionSource<object>>> outgoing = new Queue<Tuple<object, bool, TaskCompletionSource<object>>>();

            private int sentCount, receivedCount;
            public int SentCount
            {
                get { return Thread.VolatileRead(ref sentCount); }
            }
            public int ReceivedCount
            {
                get { return Thread.VolatileRead(ref receivedCount); }
            }

            private long lastSeen;

            public DateTime LastSeen
            {
                get { return DateTime.FromBinary(Interlocked.Read(ref lastSeen)); }
            }
            private void Seen()
            {
                Interlocked.Exchange(ref lastSeen, DateTime.UtcNow.ToBinary());
            }
            internal void SetResult(object result)
            {
                Seen();
                TaskCompletionSource<object> task;
                lock(outgoing)
                {
                    task = incoming.Dequeue();
                    receivedCount++;
                }
                try
                {
                    task.SetResult(result);
                } catch(Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                }
            }
            internal Tuple<object, bool, TaskCompletionSource<object>>[] DequeueUnsent()
            {
                lock (outgoing)
                {
                    var arr = outgoing.ToArray();
                    outgoing.Clear();
                    return arr;
                }
            }
            internal TaskCompletionSource<object>[] DequeueSent()
            {
                lock (outgoing) // all locks on that
                {
                    var arr = incoming.ToArray();
                    incoming.Clear();
                    return arr;
                }
            }
            public bool RequestOutgoing()
            {
                Tuple<object, bool, TaskCompletionSource<object>> next;
#if DEBUG
                try
                {
#endif
                    lock (outgoing)
                    {
                        if (outgoing.Count > 0)
                        {
                            next = outgoing.Dequeue();
                            if (next.Item2) incoming.Enqueue(next.Item3);
                            var tmpHandler = handler;
                            var tmpConn = connection;
                            if (tmpHandler == null || tmpConn == null) return false;

                            tmpConn.Send(tmpHandler.context, next.Item1);
                            sentCount++;
                            return true;
                        }
                    }
                    return false;
#if DEBUG
                } catch
                {
                    if(Debugger.IsAttached) Debugger.Break();
                    throw;
                }
#endif
            }
            public Task<object> Execute(object obj, bool expectReply, object state = null)
            {
                if(obj == null) throw new ArgumentNullException("obj");
                var taskSource = expectReply ? new TaskCompletionSource<object>(state) : null;
                var tuple = Tuple.Create(obj, expectReply, taskSource);
                ExecuteImpl(tuple);
                return taskSource == null ? null : taskSource.Task;
            }

            internal void ExecuteImpl(Tuple<object, bool, TaskCompletionSource<object>> tuple)
            {
                lock (outgoing)
                {
                    if (!isReady) throw new InvalidOperationException("Socket is not open");
                    outgoing.Enqueue(tuple);
                }
                connection.PromptToSend(handler.context);
                
            }
            internal int GetQueueLength()
            {
                // returns -1 if unhealthy, otherwise: the number of outstanding items
                if(isReady)
                {
                    lock(outgoing)
                    {
                        if (isReady)
                        {
                            return incoming.Count + outgoing.Count;
                        }
                    }
                }
                return -1;
            }

            private readonly TcpHandler handler;
            private readonly IPEndPoint endpoint;
            public ClientNode(TcpHandler handler, IPEndPoint endpoint)
            {
                Seen();
                if (endpoint == null) throw new ArgumentNullException("endpoint");
                if (handler == null) throw new ArgumentNullException("handler");
                this.handler = handler;
                this.endpoint = endpoint;
            }
            public IPEndPoint EndPoint { get { return endpoint; } }
            private Connection connection;
            private volatile bool isReady;
            public bool IsReady
            {
                get { return isReady; }
                private set { isReady = value; }
            }
            public Exception TryOpen(Action<Connection> connectionInitializer)
            {
                try
                {
                    var tmp = handler.OpenConnection(endpoint, connectionInitializer);
                    tmp.UserToken = this; // want quick access to the queues
                    connection = tmp;
                    IsReady = true;
                    return null;
                }
                catch(Exception ex)
                {
                    IsReady = false;
                    return ex;
                }
            }
            public event EventHandler Closed;
            protected void OnClosed()
            {
                var tmp = Closed;
                if (tmp != null) tmp(this, EventArgs.Empty);
            }
            public void Close()
            {
                IsReady = false;
                var tmp = connection;
                connection = null;
                try
                {
                    handler.CloseConnection(tmp);
                } catch(Exception ex) {Trace.WriteLine(ex.Message);}
                OnClosed();
            }
        }    
    }
    

}
