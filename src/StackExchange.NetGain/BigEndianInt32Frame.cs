
using System.IO;

namespace StackExchange.NetGain
{
    public class BigEndianInt32Frame : IFrame
    {
        private readonly int value;
        private readonly bool flush;
        public BigEndianInt32Frame(int value, bool flush = true)
        {
            this.value = value;
            this.flush = flush;
        }
        public bool Flush { get { return flush; } }
        protected virtual void Write(NetContext context, Connection connection, Stream stream)
        {
            byte[] buffer = null;
            try
            {
                buffer = context.GetBuffer();
                buffer[0] = (byte) (value >> 24);
                buffer[1] = (byte) (value >> 16);
                buffer[2] = (byte) (value >> 8);
                buffer[3] = (byte) (value);
                stream.Write(buffer, 0, 4);
            } finally
            {
                context.Recycle(buffer);   
            }
        }
        void IFrame.Write(NetContext context, Connection connection, Stream stream)
        {
            Write(context, connection, stream);
        }
    }
}