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
    //internal class EchoProcessor : IMessageProcessor
    //{
    //    public string Name { get { return "Echo"; } }
    //    public string Description { get { return "Garbage in, garbage out"; } }
    //    void IMessageProcessor.StartProcessor(string configuration) {}
    //    void IMessageProcessor.EndProcessor() { }
    //    void IMessageProcessor.Heartbeat() { }
    //    object IMessageProcessor.StartClient()
    //    {
    //        return null;
    //    }

    //    void IMessageProcessor.Received(NetContext context, Connection connection, object message)
    //    {
    //        connection.Send(context, message);
    //    }

    //    void IMessageProcessor.EndClient(object state)
    //    {
    //    }
    //    void IDisposable.Dispose()
    //    {
    //    }
    //}
    //internal class EchoClient : TcpClient
    //{
    //    protected override void Serialize(object obj, Stream destination)
    //    {
    //        var bytes = Encoding.UTF8.GetBytes((string) obj);
    //        destination.Write(bytes, 0, bytes.Length);
    //    }
    //    protected override object Deserialize(Stream source)
    //    {
    //        using (var reader = new StreamReader(source))
    //            return reader.ReadToEnd();
    //    }
    //}

   
}
