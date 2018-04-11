
using System.IO;
namespace StackExchange.NetGain
{
    public interface IProtocolProcessor
    {
        int ProcessIncoming(NetContext context, Connection connection, Stream incomingBuffer);
        void Send(NetContext context, Connection connection, object message);
        IFrame GetOutboundFrame(NetContext context);
        void InitializeClientHandshake(NetContext context, Connection connection);
        void GracefulShutdown(NetContext context, Connection connection);

        void InitializeInbound(NetContext context, Connection connection);
        void InitializeOutbound(NetContext context, Connection connection);
    }
}
