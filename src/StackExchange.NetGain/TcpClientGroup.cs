using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.NetGain
{
    public class TcpClientGroup : TcpHandler
    {
        public override string ToString()
        {
            return "client-group";
        }

        public TcpClientGroup()
        {
            WaitTimeout = 1000;
        }
        public Tuple<int, int>[] GetCounts()
        {
            return Array.ConvertAll(nodes, node => Tuple.Create(node.SentCount, node.ReceivedCount));
        }
        public int WaitTimeout { get; set; }
        public object Wait(Task<object> task, int timeout = -1)
        {
            return TcpClient.WaitImpl(task, timeout < 0 ? WaitTimeout : timeout);
        }

        private ClientNode[] nodes;

        public void Open(IPEndPoint[] endpoints, Action<Connection> connectionInitializer = null)
        {
            if(endpoints== null || endpoints.Length == 0) throw new ArgumentNullException("endpoints");
            
            nodes = new ClientNode[endpoints.Length];
            EventHandler clientClosed = ClientClosed;
            for(int i = 0 ; i < endpoints.Length ; i++)
            {
                var node = new ClientNode(this, endpoints[i]);
                node.Closed += clientClosed;
                nodes[i] = node;
                Exception ex = node.TryOpen(connectionInitializer);
                if(ex != null)
                {
                    OnConnectFailed(node.EndPoint, ex);
                    Trace.WriteLine(ex.Message);
                }
            }
            UpdateActiveNodeCount();
            timer = new Timer(Heartbeat, null, PollFrequency, PollFrequency);
        }

        public DateTime? GetLastSeen(IPEndPoint endpoint, out int count)
        {
            foreach(var node in nodes)
            {
                if (node != null && Equals(node.EndPoint, endpoint))
                {
                    count = node.ReceivedCount;
                    return node.LastSeen;
                }
            }
            count = 0;
            return null;
        }
        private const int PollFrequency = 15000;
        private void Heartbeat(object state)
        {
            var arr = nodes;
            if (arr == null)
            {
                activeNodes = 0;
                return;
            }
            for(int i = 0 ; i < arr.Length; i++)
            {
                var node = arr[i];
                if (node.IsReady) continue;

                try
                {
                    Trace.WriteLine(string.Format("Attempting to reconnect to {0}...", node.EndPoint));
                    Exception ex;
                    if((ex = node.TryOpen(null)) == null) Trace.WriteLine(string.Format("SUCCESS ({0} open)", node.EndPoint));
                    else
                    {
                        OnConnectFailed(node.EndPoint, ex);
                        Trace.WriteLine(string.Format("FAILED ({0} closed): {1}", node.EndPoint, ex.Message));
                    }
                    UpdateActiveNodeCount(); // happy to do this each time; not a problem - very cheap
                }
                catch (Exception ex)
                { // woah, not expecting this level of crazy
                    Trace.WriteLine(ex.Message);
                }
            }
        }

        public event Action<IPEndPoint,Exception> ConnectFailed;
        protected virtual void OnConnectFailed(IPEndPoint endpoint, Exception ex)
        {
            var handler = ConnectFailed;
            if (handler != null) handler(endpoint, ex);
        }

        private Timer timer;
        private void ClientClosed(object sender, EventArgs args)
        {
            var node = sender as ClientNode;
            if (node == null) return; // just... no

            UpdateActiveNodeCount();

            Trace.WriteLine(string.Format("Disconned from {0}", node.EndPoint));

            // re-distribute any unsent work
            var unsent = node.DequeueUnsent();

            for (int i = 0; i < unsent.Length; i++)
            {
                var client = GetClient();
                if (client == null)
                {
                    unsent[i].Item3.TrySetException(CreateAllClientsDeadFail());
                }
                else
                {
                    client.ExecuteImpl(unsent[i]);
                }
            }
            var sent = node.DequeueSent();
            for(int i = 0; i < sent.Length ; i++)
            {
                sent[i].TrySetException(CreateOperationAbortedFail());
            }
        }
        private int roundRobin = -1;
        
        private ClientNode GetClient()
        {
            int startIndex = Interlocked.Increment(ref roundRobin) % nodes.Length;

            // first look to see who is completely free (not actively writing)
            // otherwise we'll look for the shortest queue
            int bestIndex = -1, bestLength = int.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                int index = (startIndex + i) % nodes.Length;

                int len = nodes[index].GetQueueLength();
                if (len == 0)
                {
                    // idle; that'll do!
                    return nodes[index];
                }
                if (len > 0)
                {
                    // work outstanding, but healthy
                    if (len < bestLength)
                    {
                        bestLength = len;
                        bestIndex = index;
                    }
                }
            }
            return bestIndex >= 0 ? nodes[bestIndex] : null;
        }
        private Exception CreateAllClientsDeadFail()
        {
            return new InvalidOperationException("All tcp clients are failing");
        }
        private Exception CreateOperationAbortedFail()
        {
            return new InvalidOperationException("This operation was sent, but was aborted due to a critical communication failure");
        }
        public Task<object> Execute(object message, bool expectReply = true, object state = null)
        {
            var node = GetClient();
            if (node == null) throw CreateAllClientsDeadFail();
            return node.Execute(message, expectReply, state);
        }
        public object ExecuteSync(object message, int timeout = -1)
        {
            var task = Execute(message, true, null);
            return Wait(task, timeout);
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        nodes[i].Close();
                    }
                    nodes = null;
                }
                UpdateActiveNodeCount();
                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }
            }
            base.Dispose(disposing);
        }
        
        public void Kill(int clientIndex)
        {
            nodes[clientIndex].Close();
            UpdateActiveNodeCount();
        }

        private volatile int activeNodes;
        public int ActiveNodes
        {
            get { return activeNodes; }
        }
        private void UpdateActiveNodeCount()
        {
            var tmp = nodes;
            int count = 0;
            if(tmp != null)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if(node != null && node.IsReady) count++;
                }
            }
            activeNodes = count;
        }
    }
}
