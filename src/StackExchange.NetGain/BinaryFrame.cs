using System;
using System.IO;

namespace StackExchange.NetGain
{
    public class BinaryFrame : IFrame
    {
        private readonly byte[] value;
        public BinaryFrame(byte[] value, bool flush = true)
        {
            if (value == null) throw new ArgumentNullException("value");
            this.flush = flush;
            this.value = value;
        }
        private readonly bool flush;
        public bool Flush { get { return flush; } }
        protected virtual void Write(NetContext context, Connection connection, Stream stream)
        {
            stream.Write(value, 0, value.Length);
        }
        void IFrame.Write(NetContext context, Connection connection, Stream stream)
        {
            Write(context, connection, stream);
        }
        public override string ToString()
        {
            return value.Length + " bytes";
        }
    }
}
