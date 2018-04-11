using System;
using System.IO;
using System.Text;

namespace StackExchange.NetGain
{
    class BufferStream : Stream
    {
        private NetContext context;
        internal BufferStream(NetContext context, int maxLength)
        {
            if(context == null) throw new ArgumentNullException("context");
            this.context = context;
            this.maxLength = maxLength;
            buffers = context.GetCircularBuffer();
        }

        private int length, offset, origin;
        private bool isReadOnly;
        internal bool IsReadonly { get { return isReadOnly; } set { isReadOnly = value; } }
        private CircularBuffer<byte[]> buffers;
        private void CheckDisposed()
        {
            if(context == null)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
        public void Reset()
        {
            CheckDisposed();
            length = offset = origin = 0;
            // trim off any silly numbers
            while(buffers.Count > 10) context.Recycle(buffers.Pop());
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if (context != null) context.Recycle(buffers);
                context = null;
                buffers = null;
            }
            base.Dispose(disposing);
        }

        public override void SetLength(long value)
        {
            CheckDisposed();
            if(isReadOnly) throw new NotSupportedException();
            var val = checked((int) value);
            if(val < 0) throw new ArgumentOutOfRangeException("value");

            if (value <= length)
            {
                length = val;
                if (Position > length) Position = length;
            }
            else
            {
                Grow(val);
                length = val;
            }            
        }
        private readonly int maxLength;
        private void Grow(int newLength)
        {
            if (maxLength > 0 && newLength > maxLength)
            {
                throw new InvalidOperationException(string.Format("Buffer maximum length exceeded ({0} vs {1})",
                    newLength, maxLength));
            }
            int spaceRequired = checked(newLength + offset);
            int chunkCount = spaceRequired / NetContext.BufferSize;
            if ((spaceRequired % NetContext.BufferSize) != 0) chunkCount++;
            while (buffers.Count < chunkCount) buffers.Push(context.GetBuffer());
        }
        public void Discard(int count)
        {
            CheckDisposed();
            if(count > length) throw new ArgumentOutOfRangeException("count");
            if(count == length)
            { // all going!
                Reset();
            } else
            {
                origin += count;
                int dropPages = origin/NetContext.BufferSize;
                while(dropPages > 0)
                {
                    context.Recycle(buffers.Pop());
                    dropPages--;
                }
                origin = origin%NetContext.BufferSize;
                length -= count;
                Position = 0;
            }
        }
        public override void Write(byte[] buffer, int bufferOffset, int count)
        {
            int origCount = count;
            CheckDisposed();
            if(isReadOnly) throw new NotSupportedException();

            int newEnd = checked((offset - origin) + count);
            if (newEnd > length)
            {
                Grow(newEnd);
                length = newEnd;
            }
            
            int chunkIndex = offset/NetContext.BufferSize, chunkOffet = offset%NetContext.BufferSize;
            int thisPage = NetContext.BufferSize - chunkOffet;
            var chunk = buffers[chunkIndex];
            if (thisPage >= count)
            { 
                // can write to a single page
                Buffer.BlockCopy(buffer, bufferOffset, chunk, chunkOffet, count);
            }
            else
            { // need multiple pages; let's use up the current page to start...
                Buffer.BlockCopy(buffer, bufferOffset, chunk, chunkOffet, thisPage);
                bufferOffset += thisPage;
                count -= thisPage;

                while (count > 0)
                {
                    thisPage = count > NetContext.BufferSize ? NetContext.BufferSize : count;
                    chunk = buffers[++chunkIndex];
                    Buffer.BlockCopy(buffer, bufferOffset, chunk, 0, thisPage);
                    bufferOffset += thisPage;
                    count -= thisPage;
                }
            }
            offset += origCount;
        }
        public override int Read(byte[] buffer, int bufferOffset, int count)
        {
            CheckDisposed();
            int remaining = length - (offset - origin);
            if (remaining <= 0 || count <= 0) return 0;
            if (remaining < count) count = remaining;

            int chunkIndex = offset/NetContext.BufferSize, chunkOffet = offset%NetContext.BufferSize, read;
            int thisPage = NetContext.BufferSize - chunkOffet;
            var chunk = buffers[chunkIndex];
            if(thisPage >= count)
            { 
                // can fill from a single page
                Buffer.BlockCopy(chunk, chunkOffet, buffer, bufferOffset, count);
                read = count;
            } else
            { 
                // need multiple pages; let's use up the current page to start...
                Buffer.BlockCopy(chunk, chunkOffet, buffer, bufferOffset, thisPage);
                bufferOffset += thisPage;
                read = thisPage;
                count -= thisPage;

                while(count > 0)
                {
                    thisPage = count > NetContext.BufferSize ? NetContext.BufferSize : count;
                    chunk = buffers[++chunkIndex];
                    Buffer.BlockCopy(chunk, 0, buffer, bufferOffset, thisPage);
                    bufferOffset += thisPage;
                    read += thisPage;
                    count -= thisPage;
                }
            }
            offset += read;
            return read;
        }
        public override long Position
        {
            get { return offset - origin; }
            set
            {
                if (value < 0 || value > length) throw new ArgumentOutOfRangeException("value");
                offset = ((int) value) + origin;
            }
        }

        public override bool CanSeek { get { return true; } }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return !isReadOnly; } }
        public override bool CanTimeout { get { return false; } }
        public override long Length { get { return length; } }
        public override void Flush() { /* no-op */ }
        public override long Seek(long seekOffset, SeekOrigin seekOrigin)
        {
            switch (seekOrigin)
            {
                case SeekOrigin.Begin:
                    Position = seekOffset;
                    break;
                case SeekOrigin.Current:
                    Position += seekOffset;
                    break;
                case SeekOrigin.End:
                    Position = length + seekOffset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("origin");
            }
            return Position;
        }



        internal string Dump()
        {
            long oldPos = Position;
            string s;
            using(var ms = new MemoryStream())
            {
                Position = 0;
                CopyTo(ms);
                s = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int) ms.Length);
            }
            Position = oldPos;
            return s;
        }
    }
}
