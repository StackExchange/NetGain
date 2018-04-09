
using System.Net;
namespace StackExchange.NetGain
{
    public class BasicBinaryProtocolFactory : IProtocolFactory
    {
        private static readonly BasicBinaryProtocolFactory @default = new BasicBinaryProtocolFactory();
        public static BasicBinaryProtocolFactory Default { get { return @default; } }
        private BasicBinaryProtocolFactory() { }

        IProtocolProcessor IProtocolFactory.GetProcessor()
        {
            return new BasicBinaryProtocol();
        }

        Connection IProtocolFactory.CreateConnection(EndPoint endpoint)
        {
            return null;
        }

        public class BasicBinaryProtocol : ProtocolProcessor
        {
            protected override int ProcessIncoming(NetContext context, Connection connection, System.IO.Stream incomingBuffer)
            {
                if (incomingBuffer.Length < 4) return 0;
                byte[] length = new byte[4];
                NetContext.Fill(incomingBuffer, length, 4);

                int len = (length[0] << 24) | (length[1] << 16) | (length[2] << 8) | (length[3]);
                if (incomingBuffer.Length < len + 4) return 0;

                byte[] blob = new byte[len];
                NetContext.Fill(incomingBuffer, blob, len);
                context.Handler.OnReceived(connection, blob);
                return len + 4;
            }
            protected override void Send(NetContext context, Connection connection, object message)
            {
                byte[] blob = (byte[])message, length = new byte[4];
                int len = blob.Length; // big-endian
                length[0] = (byte)(len >> 24);
                length[1] = (byte)(len >> 16);
                length[2] = (byte)(len >> 8);
                length[3] = (byte)(len);
                EnqueueFrame(context, new BinaryFrame(length, false));
                EnqueueFrame(context, new BinaryFrame(blob, true));
            }
        }
    }
}
