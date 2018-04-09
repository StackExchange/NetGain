
using System.Net;
namespace StackExchange.NetGain
{
    public interface IProtocolFactory
    {
        IProtocolProcessor GetProcessor();
        Connection CreateConnection(EndPoint endpoint);
    }
}
