using System;
using System.IO;
using System.Text;

namespace StackExchange.NetGain
{
    public class StringFrame : IFrame
    {
        private readonly Encoding encoding;
        private readonly string value;
        public StringFrame(string value, Encoding encoding = null, bool flush = true)
        {
            if(value == null) throw new ArgumentNullException("value");
            this.value = value;
            this.flush = flush;
            this.encoding = encoding ?? Encoding.UTF8;
        }
        public override string ToString()
        {
            return value;
        }
        private readonly bool flush;
        public bool Flush { get { return flush; } }
        protected virtual void Write(NetContext context, Connection connection, Stream stream)
        {
            byte[] buffer = null;
            try
            {
                buffer = context.GetBuffer();
                if(encoding.GetMaxByteCount(value.Length) <= buffer.Length || encoding.GetByteCount(value) <= buffer.Length)
                {
                    int len = encoding.GetBytes(value, 0, value.Length, buffer, 0);
                    stream.Write(buffer, 0, len);
                } else
                { // need to do things the hard way...
                    throw new NotImplementedException();
                }
            }
            finally
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
