
using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace StackExchange.NetGain
{
    public class NetContext
    {
        public object[] Extensions { get; set; }


        private readonly MicroPool<byte[]> bufferPool = new MicroPool<byte[]>(256);
        private readonly MicroPool<CircularBuffer<byte[]>> circularBufferPool = new MicroPool<CircularBuffer<byte[]>>(64);
        private readonly MicroPool<SocketAsyncEventArgs> argsPool = new MicroPool<SocketAsyncEventArgs>(64);
        private readonly EventHandler<SocketAsyncEventArgs> asyncHandler;

        internal static int TryFill(Stream source, byte[] buffer, int count)
        {
            int read, offset = 0, totalRead = 0;
            while(count > 0 && (read = source.Read(buffer, offset, count)) > 0)
            {
                count -= read;
                offset += read;
                totalRead += read;
            }
            return totalRead;
        }
        internal static void Fill(Stream source, byte[] buffer, int count)
        {
            int read = TryFill(source, buffer, count);
            if(read != count) throw new EndOfStreamException();
        }

        internal void DoNotAccept()
        {
            var handler = this.Handler;
            if (handler != null) handler.DoNotAccept();
        }


        private readonly TcpHandler handler;
        public TcpHandler Handler { get { return handler; } }
        public NetContext(EventHandler<SocketAsyncEventArgs> asyncHandler, TcpHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            if (asyncHandler == null) throw new ArgumentNullException("asyncHandler");
            this.asyncHandler = asyncHandler;
            this.handler = handler;
        }
        private static readonly RandomNumberGenerator rng = new RNGCryptoServiceProvider();
        public static void GetRandomBytes(byte[] buffer)
        {  
            // "This method is thread safe." http://msdn.microsoft.com/en-us/library/system.security.cryptography.rngcryptoserviceprovider.getbytes.aspx
            rng.GetBytes(buffer);
        }

        public SocketAsyncEventArgs GetSocketArgs()
        {
            var args = argsPool.TryGet();
            if (args == null)
            {
                args = new SocketAsyncEventArgs();
                args.Completed += asyncHandler;
            }
            return args;
        }
        public byte[] GetBuffer()
        {
            return bufferPool.TryGet() ?? new byte[BufferSize];
        }
        internal CircularBuffer<byte[]> GetCircularBuffer()
        {
            return circularBufferPool.TryGet() ?? new CircularBuffer<byte[]>();
        }

        public const int BufferSize = 4096;
        internal void Recycle(CircularBuffer<byte[]> buffer)
        {
            if (buffer != null)
            {
                while (buffer.Count != 0) Recycle(buffer.Pop());
            }
        }
        public void Recycle(byte[] buffer)
        {
            if (buffer != null && buffer.Length == BufferSize)
            {
                bufferPool.PutBack(buffer);
            }
        }
        public void Recycle(SocketAsyncEventArgs args)
        {
            if (args != null)
            {
                args.AcceptSocket = null;
                var buffer = args.Buffer;
                int count = args.Count;
                args.SetBuffer(null, 0, 0);
                
                if (buffer == null)
                {
                    // nada nix and nothing to do
                }
                else if (count != BufferSize && args.LastOperation == SocketAsyncOperation.Receive)
                {
                    // don't recycle it! it was a micro-buffer for low-weight read; the rest
                    // of the array is not owned by us
                } 
                else
                {
                    Recycle(buffer);
                }

                argsPool.PutBack(args);
            }
        }

        private readonly MicroPool<CircularBuffer<IFrame>> frameBufferPool = new MicroPool<CircularBuffer<IFrame>>(64);
        internal CircularBuffer<IFrame> GetFrameBuffer()
        {
            return frameBufferPool.TryGet() ?? new CircularBuffer<IFrame>();
        }
        public void Recycle(CircularBuffer<IFrame> buffer)
        {
            buffer.Reset();
            frameBufferPool.PutBack(buffer);
        }
    }
}
