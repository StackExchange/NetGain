using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;

namespace StackExchange.NetGain
{
    public class TcpServer : TcpHandler
    {
        private IMessageProcessor messageProcessor;
        public IMessageProcessor MessageProcessor { get { return messageProcessor; } set { messageProcessor = value; } }

        private Tuple<Socket, EndPoint>[] connectSockets;
        public TcpServer(int concurrentOperations = 0) : base(concurrentOperations)
        {
            
        }
        
        public override string ToString()
        {
            return "server";
        }

        protected override void Close()
        {
            Stop();
            base.Close();
        }
        private volatile bool stopped;
        public bool IsStopped {  get {  return stopped; } }
        public void Stop()
        {
            stopped = true;
            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }
            var ctx = Context;
            if (ctx != null) ctx.DoNotAccept();

            var proc = MessageProcessor;
            if (proc != null)
            {
                Log?.WriteLine("{0}\tShutting down connections...", Connection.GetLogIdent());
                foreach (var pair in allConnections)
                {
                    var conn = pair.Value;
                    try
                    {
                        proc.OnShutdown(ctx, conn); // processor first
                        conn.GracefulShutdown(ctx); // then protocol
                    }
                    catch (Exception ex)
                    { ErrorLog?.WriteLine("{0}\t{1}", Connection.GetIdent(conn), ex.Message); }
                }
                Thread.Sleep(100);
            }
            
            foreach (var pair in allConnections)
            {
                var conn = pair.Value;
                var socket = conn.Socket;
                if(socket != null)
                {
                    try { socket.Close(); }
                    catch (Exception ex) { ErrorLog?.WriteLine("{0}\t{1}", Connection.GetIdent(conn), ex.Message); }
                    try { ((IDisposable)socket).Dispose(); }
                    catch (Exception ex) { ErrorLog?.WriteLine("{0}\t{1}", Connection.GetIdent(conn), ex.Message); }
                }
            }
            if (connectSockets != null)
            {
                foreach (var tuple in connectSockets)
                {
                    var connectSocket = tuple.Item1;
                    if (connectSocket == null) continue;
                    EndPoint endpoint = null;
                    
                    try
                    {
                        endpoint = connectSocket.LocalEndPoint;
                        Log?.WriteLine("{0}\tService stopping: {1}", Connection.GetConnectIdent(endpoint), endpoint);
                        connectSocket.Close();
                    }
                    catch (Exception ex) { ErrorLog?.WriteLine("{0}\t{1}", Connection.GetConnectIdent(endpoint), ex.Message); }
                    try { ((IDisposable)connectSocket).Dispose(); }
                    catch (Exception ex) { ErrorLog?.WriteLine("{0}\t{1}", Connection.GetConnectIdent(endpoint), ex.Message); }
                }
                connectSockets = null;
                var tmp = messageProcessor;
                if(tmp != null) tmp.EndProcessor(Context);
            }
            WriteLog();
        }
        ConcurrentDictionary<long,Connection> allConnections = new ConcurrentDictionary<long,Connection>();
        
        private System.Threading.Timer timer;
        public int Backlog { get; set; }
        
        private const int LogFrequency = 10000;

        public void Start(string configuration, params IPEndPoint[] endpoints)
        {
            if (endpoints == null || endpoints.Length == 0) throw new ArgumentNullException("endpoints");

            if (connectSockets != null) throw new InvalidOperationException("Already started");

            connectSockets = new Tuple<Socket, EndPoint>[endpoints.Length];
            var tmp = messageProcessor;
            if(tmp != null) tmp.StartProcessor(Context, configuration);
            for (int i = 0; i < endpoints.Length; i++)
            {
                Log?.WriteLine("{0}\tService starting: {1}", Connection.GetConnectIdent(endpoints[i]), endpoints[i]);
                EndPoint endpoint = endpoints[i];
                Socket connectSocket = StartAcceptListener(endpoint);
                if (connectSocket == null) throw new InvalidOperationException("Unable to start all endpoints");
                connectSockets[i] = Tuple.Create(connectSocket, endpoint);
            }

            timer = new System.Threading.Timer(Heartbeat, null, LogFrequency, LogFrequency);
        }

        private void ResurrectDeadListeners()
        {
            if (IsStopped) return;
            bool haveLock = false;
            try
            {
                Monitor.TryEnter(connectSockets, 100, ref haveLock);
                if(haveLock)
                {
                    for(int i = 0; i < connectSockets.Length;i++)
                    {
                        if(connectSockets[i].Item1 == null)
                        {
                            ResurrectListenerAlreadyHaveLock(i);
                        }
                    }
                }
            }
            finally
            {
                if (haveLock) Monitor.Exit(connectSockets);
            }
        }

        void ResurrectListenerAlreadyHaveLock(int i)
        {
            var endpoint = connectSockets[i].Item2;
            try
            {
                ErrorLog?.WriteLine("Restarting listener on " + endpoint + "...");
                var newSocket = StartAcceptListener(endpoint);
                connectSockets[i] = Tuple.Create(newSocket, endpoint);
                if (newSocket == null)
                {
                    ErrorLog?.WriteLine("Unable to restart listener on " + endpoint + "...");
                }
                else
                {
                    ErrorLog?.WriteLine("Restarted listener on " + endpoint + "...");
                }
            }
            catch (Exception ex)
            {
                ErrorLog?.WriteLine("Restart failed on " + endpoint + ":" + ex.Message);
            }
        }
        protected override void OnAcceptFailed(SocketAsyncEventArgs args, Socket socket)
        {
            if (IsStopped) return;
            try
            {
                base.OnAcceptFailed(args, socket);

                ErrorLog?.WriteLine("Listener failure: " + args.SocketError);

                lock(connectSockets)
                {
                    for (int i = 0; i < connectSockets.Length; i++)
                    {
                        if (ReferenceEquals(connectSockets[i].Item1, socket))
                        {
                            // clearly mark it as dead (makes it easy to spot in heartbeat)
                            connectSockets[i] = Tuple.Create((Socket)null, connectSockets[i].Item2);

                            if (ImmediateReconnectListeners)
                            {
                                // try to resurrect promptly, but there's a good chance this will fail
                                // and will be handled by heart-beat
                                ResurrectListenerAlreadyHaveLock(i);
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                ErrorLog?.WriteLine("Epic fail in OnAcceptFailed: " + ex.Message);
            }
        }

        public void KillAllListeners()
        {
            lock(connectSockets)
            {
                for (int i = 0; i < connectSockets.Length; i++)
                {
                    var tuple = connectSockets[i];
                    var sock = tuple == null ? null : tuple.Item1;
                    if (sock != null) sock.Close();
                }
            }
        }

        private Socket StartAcceptListener(EndPoint endpoint)
        {
            try
            {
                var connectSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                connectSocket.Bind(endpoint);
                connectSocket.Listen(Backlog);
                var args = Context.GetSocketArgs();
                args.UserToken = connectSocket; // the state on each connect attempt is the originating socket
                StartAccept(args);
                return connectSocket;
            } catch(Exception ex)
            {
                ErrorLog?.WriteLine("Unable to start listener on " + endpoint.ToString() + ": " + ex.Message);
                return null;
            }
        }

        public override void OnAuthenticate(Connection connection, StringDictionary claims)
        {
            var tmp = messageProcessor;
            if(tmp != null) tmp.Authenticate(Context, connection, claims);
            base.OnAuthenticate(connection, claims);
        }
        public override void OnAfterAuthenticate(Connection connection)
        {
            var tmp = messageProcessor;
            if (tmp != null) tmp.AfterAuthenticate(Context, connection);
            base.OnAfterAuthenticate(connection);
        }
        protected override void OnAccepted(Connection connection)
        {
            var tmp = messageProcessor;
            if (tmp != null) tmp.OpenConnection(Context, connection);
            allConnections.TryAdd(connection.Id, connection);
            
            StartReading(connection);
        }

        protected override void OnFlushed(Connection connection)
        {
            var tmp = messageProcessor;
            if (tmp != null) tmp.Flushed(Context, connection);
            base.OnFlushed(connection);
        }
        protected override int GetCurrentConnectionCount()
        {
            return allConnections.Count;
        }
        protected internal override void OnClosing(Connection connection)
        {
            Connection found;
            if (allConnections.TryRemove(connection.Id, out found) && (object)found == (object)connection)
            {
                var tmp = messageProcessor;
                if (tmp != null) tmp.CloseConnection(Context, connection);
                // anything else we should do at connection shutdown
            }
            base.OnClosing(connection);
        }
        public override void OnReceived(Connection connection, object value)
        {
            var proc = messageProcessor;
            if(proc != null) proc.Received(Context, connection, value);
            base.OnReceived(connection, value);
        }

        private readonly object heartbeatLock = new object();
        public void Heartbeat()
        {
            lock(heartbeatLock) // don't want timer and manual invoke conflicting
            {
                var tmp = messageProcessor;
                if (tmp != null)
                {
                    try
                    {
                        tmp.Heartbeat(Context);
                    }
                    catch (Exception ex)
                    {
                        ErrorLog?.WriteLine("{0}\tHeartbeat: {1}", Connection.GetHeartbeatIdent(), ex.Message);
                    }
                }
                ResurrectDeadListeners();
                WriteLog();
            }
        }
        private bool immediateReconnectListeners = true;
        public bool ImmediateReconnectListeners
        {
            get { return immediateReconnectListeners; }
            set { immediateReconnectListeners = value; }
        }
        private void Heartbeat(object sender)
        {
            Heartbeat();
        }


        private int broadcastCounter;
        public override string BuildLog()
        {
            var proc = messageProcessor;
            string procStatus = proc == null ? "" : proc.ToString();
            return base.BuildLog() + " bc:" + Thread.VolatileRead(ref broadcastCounter) + " " + procStatus;
        }
        void BroadcastProcessIterator(IEnumerator<Connection> iterator, Func<Connection, object> selector, NetContext ctx)
        {

            bool cont;
            do
            {
                Connection conn;
                lock (iterator)
                {
                    cont = iterator.MoveNext();
                    conn = cont ? iterator.Current : null;
                }
                try
                {
                    if (cont && conn != null && conn.IsAlive)
                    {
                        var message = selector(conn);
                        if (message != null)
                        {
                            conn.Send(ctx, message);
                            Interlocked.Increment(ref broadcastCounter);
                        }
                    }
                }
                catch
                { // if an individual connection errors... KILL IT! and then gulp down the exception
                    try { conn.Shutdown(ctx); }
                    catch { }
                }
            } while (cont);
        }

        public void Broadcast(Func<Connection, object> selector)
        {
            // manually dual-threaded; was using Parallel.ForEach, but that caused unconstrained memory growth
            var ctx = Context;
            using (var iter = allConnections.Values.GetEnumerator())
            using (var workerDone = new AutoResetEvent(false))
            {
                ThreadPool.QueueUserWorkItem(x =>
                {
                    BroadcastProcessIterator(iter, selector, ctx);
                    workerDone.Set();
                });
                BroadcastProcessIterator(iter, selector, ctx);
                workerDone.WaitOne();
            }
        }


        public void Broadcast(object message, Func<Connection, bool> predicate = null)
        {
            if (message == null)
            {
                // nothing to send
            }
            else if (predicate == null)
            {  // no test
                Broadcast(conn => message);
            }
            else
            {
                Broadcast(conn => predicate(conn) ? message : null);
            }
        }

        public int ConnectionTimeoutSeconds { get; set; }

    }
}
