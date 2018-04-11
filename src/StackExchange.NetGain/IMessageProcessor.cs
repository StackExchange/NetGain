using System;
using System.Collections.Specialized;

namespace StackExchange.NetGain
{
    public interface IMessageProcessor : IDisposable
    {
        void CloseConnection(NetContext context, Connection connection);
        void OpenConnection(NetContext context, Connection connection);
        void Authenticate(NetContext context, Connection connection, StringDictionary claims);
        void AfterAuthenticate(NetContext context, Connection connection);
        void StartProcessor(NetContext context, string configuration);
        void EndProcessor(NetContext context);
        void Heartbeat(NetContext context);
        void Received(NetContext context, Connection connection, object message);

        void Configure(TcpService service);
        void Flushed(NetContext context, Connection connection);
        string Name { get; }
        string Description { get; }

        void OnShutdown(NetContext context, Connection connection);
    }   
}
