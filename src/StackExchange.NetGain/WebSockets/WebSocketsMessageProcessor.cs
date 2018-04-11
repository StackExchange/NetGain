
using System;
using System.Collections.Specialized;
using System.Threading;
namespace StackExchange.NetGain.WebSockets
{
    public abstract class WebSocketsMessageProcessor : IMessageProcessor
    {

        private NetContext processorContext;
        public NetContext Context { get { return processorContext; } }

        void IMessageProcessor.Configure(TcpService service)
        {
            OnConfigure(service);
        }
        protected virtual void OnConfigure(TcpService service){}
        void IMessageProcessor.CloseConnection(NetContext context, Connection connection)
        {
            OnClosed((WebSocketConnection)connection);
        }

        protected virtual void OnClosed(WebSocketConnection connection)
        { }

        void IMessageProcessor.OpenConnection(NetContext context, Connection connection)
        {
            OnOpened((WebSocketConnection)connection);
        }
        protected virtual void OnOpened(WebSocketConnection connection)
        { }

        void IMessageProcessor.Authenticate(NetContext context, Connection connection, StringDictionary claims)
        {
            OnAuthenticate((WebSocketConnection)connection, claims);
        }
        protected virtual void OnAuthenticate(WebSocketConnection connection, StringDictionary claims)
        { }


        void IMessageProcessor.AfterAuthenticate(NetContext context, Connection connection)
        {
            OnAfterAuthenticate((WebSocketConnection)connection);
        }
        protected virtual void OnAfterAuthenticate(WebSocketConnection connection)
        { }

        void IMessageProcessor.StartProcessor(NetContext context, string configuration)
        {
            if (Interlocked.CompareExchange(ref processorContext, context, null) != null) throw new InvalidOperationException("Processor already has a context");
            OnStartup(configuration);
        }
        protected virtual void OnStartup(string configuration)
        { }

        void IMessageProcessor.EndProcessor(NetContext context)
        {
            OnShutdown();
            processorContext = null;
        }
        protected virtual void OnShutdown()
        {
        }

        void IMessageProcessor.Heartbeat(NetContext context)
        {
            OnHeartbeat();
        }
        protected virtual void OnHeartbeat()
        { }

        void IMessageProcessor.Received(NetContext context, Connection connection, object message)
        {
            string s;
            byte[] b;
            if((s = message as string) != null) OnReceive((WebSocketConnection)connection, s);
            else if ((b = message as byte[]) != null) OnReceive((WebSocketConnection)connection, b);
        }
        protected virtual void OnReceive(WebSocketConnection connection, string message)
        { }
        protected virtual void OnReceive(WebSocketConnection connection, byte[] message)
        { }
        string IMessageProcessor.Name
        {
            get { return Name; }
        }
        protected virtual string Name
        {
            get { return GetType().Name; }
        }

        string IMessageProcessor.Description
        {
            get { return Description; }
        }
        protected virtual string Description
        {
            get { return GetType().Name; }
        }

        void IDisposable.Dispose()
        {
            OnDispose(true);
            processorContext = null;
        }
        protected virtual void OnDispose(bool disposing)
        { }

        public void Broadcast(string message, Func<WebSocketConnection, bool> predicate = null)
        {
            Func<Connection, bool> typedPredicate = null;
            if (predicate != null) typedPredicate = c => predicate((WebSocketConnection)c);
            var srv = Server;
            if (srv != null) srv.Broadcast(message, typedPredicate);
        }
        public void Broadcast(byte[] message, Func<WebSocketConnection, bool> predicate = null)
        {
            Func<Connection, bool> typedPredicate = null;
            if(predicate != null) typedPredicate = c => predicate((WebSocketConnection)c);
            var srv = Server;
            if (srv != null) srv.Broadcast(message, typedPredicate);
        }
        public void Broadcast(Func<WebSocketConnection, string> selector)
        {
            if(selector == null) throw new ArgumentNullException("selector");
            Func<Connection, object> typedSelector = c => selector((WebSocketConnection)c);
            var srv = Server;
            if (srv != null) srv.Broadcast(typedSelector);
        }
        public void Broadcast(Func<WebSocketConnection, byte[]> selector)
        {
            if (selector == null) throw new ArgumentNullException("selector");
            Func<Connection, object> typedSelector = c => selector((WebSocketConnection) c);
            var srv = Server;
            if(srv != null) srv.Broadcast(typedSelector);
        }
        private TcpServer Server
        {
            get
            {
                var ctx = Context;
                if (ctx == null) return null;
                var server = ctx.Handler as TcpServer;
                if(server == null) throw new InvalidOperationException("This operation is only possible on a server");
                return server;
            }
        }
        public void Send(WebSocketConnection connection, string message)
        {
            if(message == null) throw new ArgumentNullException("message");
            connection.Send(Context, message);
        }
        public void Send(WebSocketConnection connection, byte[] message)
        {
            if (message == null) throw new ArgumentNullException("message");
            connection.Send(Context, message);
        }

        void IMessageProcessor.Flushed(NetContext context, Connection connection)
        {
            OnFlushed((WebSocketConnection)connection);
        }
        protected virtual void OnFlushed(WebSocketConnection connection)
        { }


        protected virtual void OnShutdown(WebSocketConnection connection)
        {
            
        }
        void IMessageProcessor.OnShutdown(NetContext context, Connection connection)
        {
            OnShutdown((WebSocketConnection)connection);
        }
    }
}
