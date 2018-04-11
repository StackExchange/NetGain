using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StackExchange.NetGain.WebSockets
{
    public sealed class WebSocketsFrame : IFrame
    {
        public static WebSocketsFrame TryParseFrameHeader(byte[] buffer, int bytesAvailable, out int headerLength)
        {
            int maskOffset;
            if (bytesAvailable < 2)
            {
                headerLength = 0;
                return null;
            }
            bool masked = (buffer[1] & 128) != 0;
            int tmp = buffer[1] & 127;
            int payloadLen;
            switch (tmp)
            {
                case 126:
                    headerLength = masked ? 8 : 4;
                    if (bytesAvailable < headerLength) return null;
                    payloadLen = (buffer[2] << 8) | buffer[3];
                    maskOffset = 4;
                    break;
                case 127:
                    headerLength = masked ? 14 : 10;
                    if (bytesAvailable < headerLength) return null;
                    int big = WebSocketsProcessor.ReadInt32(buffer, 2), little = WebSocketsProcessor.ReadInt32(buffer, 6);
                    if (big != 0 || little < 0) throw new ArgumentOutOfRangeException(); // seriously, we're not going > 2GB
                    payloadLen = little;
                    maskOffset = 10;
                    break;
                default:
                    headerLength = masked ? 6 : 2;
                    if (bytesAvailable < headerLength) return null;
                    payloadLen = tmp;
                    maskOffset = 2;
                    break;
            }

            var frame = new WebSocketsFrame();

            frame.IsFinal = (buffer[0] & 128) != 0;
            frame.Reserved1 = (buffer[0] & 64) != 0;
            frame.Reserved2 = (buffer[0] & 32) != 0;
            frame.Reserved3 = (buffer[0] & 16) != 0;
            frame.OpCode = (WebSocketsFrame.OpCodes)(buffer[0] & 15);
            frame.Mask = masked ? (int?)WebSocketsProcessor.ReadInt32(buffer, maskOffset) : null;
            frame.PayloadLength = payloadLen;

            return frame;
        }

        public override string ToString()
        {
            return OpCode + ": " + PayloadLength + " bytes (" + flags + ")";
        }
        private Flags flags = Flags.IsFinal;

        [Flags]
        private enum Flags : byte
        {
            IsFinal = 128,
            Reserved1 = 64,
            Reserved2 = 32,
            Reserved3 = 16,
            None = 0
        }

        public enum OpCodes
        {
            Continuation = 0,
            Text = 1,
            Binary = 2,
            // 3-7 reserved for non-control op-codes
            Close = 8,
            Ping = 9,
            Pong = 10,
            // 11-15 reserved for control op-codes
        }
        public WebSocketsFrame()
        { }
        private bool HasFlag(Flags flag)
        {
            return (flags & flag) != 0;
        }
        private void SetFlag(Flags flag, bool value)
        {
            if (value) flags |= flag;
            else flags &= ~flag;
        }

        public bool IsControlFrame { get { return (OpCode & OpCodes.Close) != 0; } }
        public int? Mask { get; set; }
        public OpCodes OpCode { get; set; }
        public bool IsFinal { get { return HasFlag(Flags.IsFinal); } set { SetFlag(Flags.IsFinal, value); } }
        public bool Reserved1 { get { return HasFlag(Flags.Reserved1); } set { SetFlag(Flags.Reserved1, value); } }
        public bool Reserved2 { get { return HasFlag(Flags.Reserved2); } set { SetFlag(Flags.Reserved2, value); } }
        public bool Reserved3 { get { return HasFlag(Flags.Reserved3); } set { SetFlag(Flags.Reserved3, value); } }
            
        public int PayloadLength { get; set; }
        public Stream Payload { get; set; }
        internal int GetLengthEstimate() { return PayloadLength + 2; } // doesn't need to be 100% accurate
        void IFrame.Write(NetContext context, Connection connection, Stream stream)
        {
            //Debug.WriteLine("write:" + this);
            byte[] buffer = null;
            try
            {
                buffer = context.GetBuffer();
                int offset = 0;
                buffer[offset++] = (byte)(((int)flags & 240) | ((int)OpCode & 15));
                if (PayloadLength > ushort.MaxValue)
                { // write as a 64-bit length
                    buffer[offset++] = (byte)((Mask.HasValue ? 128 : 0) | 127);
                    buffer[offset++] = 0;
                    buffer[offset++] = 0;
                    buffer[offset++] = 0;
                    buffer[offset++] = 0;
                    buffer[offset++] = (byte)(PayloadLength >> 24);
                    buffer[offset++] = (byte)(PayloadLength >> 16);
                    buffer[offset++] = (byte)(PayloadLength >> 8);
                    buffer[offset++] = (byte)(PayloadLength);
                }
                else if (PayloadLength > 125)
                { // write as a 16-bit length
                    buffer[offset++] = (byte)((Mask.HasValue ? 128 : 0) | 126);
                    buffer[offset++] = (byte)(PayloadLength >> 8);
                    buffer[offset++] = (byte)(PayloadLength);
                }
                else
                { // write in the header
                    buffer[offset++] = (byte)((Mask.HasValue ? 128 : 0) | PayloadLength);
                }
                if (Mask.HasValue)
                {
                    int mask = Mask.Value;
                    buffer[offset++] = (byte)(mask >> 24);
                    buffer[offset++] = (byte)(mask >> 16);
                    buffer[offset++] = (byte)(mask >> 8);
                    buffer[offset++] = (byte)(mask);
                }
                stream.Write(buffer, 0, offset);

                if (PayloadLength != 0)
                {
                    int totalRead = WebSocketsProcessor_RFC6455_13.Copy(Payload, stream, buffer, Mask, PayloadLength);
                    if (totalRead != PayloadLength)
                    {
                        throw new EndOfStreamException("Wrong payload length sent");
                    }
                }

            }
            finally
            {
                context.Recycle(buffer);
            }
        }

        bool IFrame.Flush
        {
            get { return IsFinal; }
        }
    }

}
