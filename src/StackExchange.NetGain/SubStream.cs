using System;
using System.IO;

namespace StackExchange.NetGain
{
    class SubStream : Stream
    {
        private readonly Stream tail;
        private readonly int length;
        private int position;
        public SubStream(Stream tail, int length)
        {
            if (tail == null) throw new ArgumentNullException("tail");
            if(length < 0) throw new ArgumentOutOfRangeException("length");
            this.tail = tail;
            this.length = length;
        }
        public override long Length
        {
            get { return length; }
        }
        public override long Position
        {
            get { return position; }
            set { if(position != value) throw new NotSupportedException(); }
        }
        public override bool CanSeek
        {
            get { return false; }
        }
        public override bool CanWrite
        {
            get { return false; }
        }
        public override bool CanRead
        {
            get { return true; }
        }
        public override bool CanTimeout
        {
            get { return false; }
        }
        public override int ReadByte()
        {
            int result = tail.ReadByte();
            if (result >= 0) position++;
            return result;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int result = tail.Read(buffer, offset, count);
            if (result > 0) position += result;
            return result;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
        public override void Flush() { }
        public override void SetLength(long value)
        {
            if(value != length) throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

    }
}
