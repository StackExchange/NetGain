using System.IO;

namespace StackExchange.NetGain
{
    public interface IFrame
    {
        void Write(NetContext context, Connection connection, Stream stream);
        bool Flush { get; }
    }
}
