using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using zlib;

namespace StackExchange.NetGain.WebSockets
{
    public class PerFrameDeflate : IExtensionFactory
    {
        public override string ToString()
        {
            long outbound = OutboundBytesSaved, inbound = InboundBytesSaved;
            return string.Format("saved i/o kB:{0}/{1}", inbound / 1024, outbound / 1024);
        }
        private long outboundBytesSaved, inboundBytesSaved;
        public long OutboundBytesSaved
        {
            get { return Interlocked.CompareExchange(ref outboundBytesSaved, 0, 0); }
        }
        public long InboundBytesSaved
        {
            get { return Interlocked.CompareExchange(ref inboundBytesSaved, 0, 0); }
        }
        private void RegisterOutboundBytesSaved(long bytes)
        {
            Interlocked.Add(ref outboundBytesSaved, bytes);
        }
        private void RegisterInboundBytesSaved(long bytes)
        {
            Interlocked.Add(ref inboundBytesSaved, bytes);
        }
        private readonly int compressMessagesLargerThanBytes;
        private readonly bool disableContextTakeover;
        private PerFrameDeflate() { }
        public static readonly IExtensionFactory Default = new PerFrameDeflate(16, true);
        bool IExtensionFactory.IsMatch(string name)
        {
            return string.Equals(name, "x-webkit-deflate-frame");
        }
        string IExtensionFactory.GetRequestHeader()
        {
            return "x-webkit-deflate-frame";
        }
        IExtension IExtensionFactory.CreateExtension(string name)
        {
            return new PerFrameDeflateImpl(this);
        }
        public PerFrameDeflate(int compressMessagesLargerThanBytes, bool disableContextTakeover)
        {
            this.compressMessagesLargerThanBytes = compressMessagesLargerThanBytes;
            this.disableContextTakeover = disableContextTakeover;
        }

        private sealed class PerFrameDeflateImpl : IExtension
        {
            private readonly PerFrameDeflate parent;
            public PerFrameDeflateImpl(PerFrameDeflate parent)
            {
                if (parent == null) throw new ArgumentNullException("parent");
                this.parent = parent;
            }
            ZStream inbound, outbound;

            string IExtension.GetResponseHeader()
            {
                return parent.disableContextTakeover ?
                    "x-webkit-deflate-frame; no_context_takeover" :
                    "x-webkit-deflate-frame";
            }
            IEnumerable<WebSocketsFrame> IExtension.ApplyIncoming(NetContext context, WebSocketConnection connection, WebSocketsFrame frame)
            {
                if (frame.Reserved1 && !frame.IsControlFrame)
                {
                    BufferStream tmp = null;
                    var payload = frame.Payload;
                    payload.Position = 0;
                    byte[] inBuffer = null, outBuffer = null;

                    try
                    {
                        outBuffer = context.GetBuffer();
                        inBuffer = context.GetBuffer();
                        tmp = new BufferStream(context, 0);

                        if (inbound == null)
                        {
                            inbound = new ZStream();
                            inbound.inflateInit();

                            // fake a zlib header with:
                            // CMF:
                            //   CM = 8 (deflate)
                            //   CINFO = 7 (32k window)
                            // FLG:
                            //   FCHECK: 26 (checksum of other bits)
                            //   FDICT: 0 (no dictionary)
                            //   FLEVEL: 3 (maximum)
                            inBuffer[0] = 120;
                            inBuffer[1] = 218;
                            inbound.next_in = inBuffer;
                            int chk = Inflate(tmp, outBuffer, 2);
                            if (chk != 0) throw new InvalidOperationException("Spoofed zlib header suggested data");
                        }   
                        
                        inbound.next_in = inBuffer;
                        int remaining = frame.PayloadLength;
                        //bool first = true;
                        while (remaining > 0)
                        {
                            int readCount = payload.Read(inBuffer, 0, inBuffer.Length);
                            if (readCount <= 0) break;
                            remaining -= readCount;

                            //if (first)
                            //{   // kill the BFINAL flag from the first block, if set; we don't want zlib
                            //    // trying to verify the ADLER checksum; unfortunately, a frame can contain
                            //    // multiple blocks, and a *later* block could have BFINAL set. That sucks.
                            //    inBuffer[0] &= 254;
                            //    first = false;
                            //}
                            Inflate(tmp, outBuffer, readCount);                          
                        }
                        if (remaining != 0) throw new EndOfStreamException();

                        // spoof the missing 4 bytes from the tail
                        inBuffer[0] = inBuffer[1] = 0x00;
                        inBuffer[2] = inBuffer[3] = 0xFF;
                        Inflate(tmp, outBuffer, 4);

                        // set our final output
                        tmp.Position = 0;
                        frame.Payload = tmp;
                        long bytesSaved = tmp.Length - frame.PayloadLength;
                        frame.PayloadLength = (int)tmp.Length;
                        frame.Reserved1 = false;
                        tmp = payload as BufferStream;
                        parent.RegisterInboundBytesSaved(bytesSaved);
                    }
#if DEBUG
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        throw;
                    }
#endif
                    finally
                    {
                        if (inbound != null)
                        {
                            inbound.next_out = null;
                            inbound.next_in = null;
                        }
                        if (tmp != null) tmp.Dispose();
                        if (inBuffer != null) context.Recycle(inBuffer);
                        if (outBuffer != null) context.Recycle(outBuffer);
                        if (parent.disableContextTakeover) ClearContext(true, false);
                    }

                }
                yield return frame;
            }

            private int Inflate(BufferStream tmp, byte[] outBuffer, int count)
            {
                inbound.next_in_index = 0;
                inbound.avail_in = count;
                inbound.next_out = outBuffer;
                int totalBytes = 0;
                do
                {
                    inbound.next_out_index = 0;
                    
                    inbound.avail_out = outBuffer.Length;

                    long priorOut = inbound.total_out;
                    var err = inbound.inflate(zlibConst.Z_SYNC_FLUSH);

                    if (err != zlibConst.Z_OK && err != zlibConst.Z_STREAM_END)
                        throw new ZStreamException("inflating: " + inbound.msg);

                    int outCount = (int)(inbound.total_out - priorOut);
                    if (outCount > 0)
                    {
                        tmp.Write(outBuffer, 0, outCount);
                        totalBytes += outCount;
                    }
                } while (inbound.avail_in > 0);
                return totalBytes;
            }

            IEnumerable<WebSocketsFrame> IExtension.ApplyOutgoing(NetContext context, WebSocketConnection connection, WebSocketsFrame frame)
            {
                if (!frame.IsControlFrame && frame.PayloadLength > parent.compressMessagesLargerThanBytes)
                {
                    if (frame.Reserved1)
                    {
                        throw new InvalidOperationException("Reserved1 flag is already set; extension conflict?");
                    }

                    int headerBytes = 0;
                    if (outbound == null)
                    {
                        outbound = new ZStream();
                        const int BITS = 12; // 4096 byte outbound buffer (instead of full 32k=15)
                        outbound.deflateInit(zlibConst.Z_BEST_COMPRESSION, BITS);
                        headerBytes = 2;
                    }
                    BufferStream tmp = null;
                    var payload = frame.Payload;
                    payload.Position = 0;
                    byte[] inBuffer = null, outBuffer = null;
                    try
                    {
                        inBuffer = context.GetBuffer();
                        outBuffer = context.GetBuffer();
                        tmp = new BufferStream(context, 0);

                        outbound.next_out = outBuffer;
                        outbound.next_in = inBuffer;

                        int remaining = frame.PayloadLength;
                        while (remaining > 0)
                        {
                            int readCount = payload.Read(inBuffer, 0, inBuffer.Length);
                            if (readCount <= 0) break;
                            remaining -= readCount;

                            outbound.next_in_index = 0;
                            outbound.avail_in = readCount;

                            do
                            {
                                outbound.next_out_index = 0;
                                outbound.avail_out = outBuffer.Length;
                                long priorOut = outbound.total_out;
                                int err = outbound.deflate(remaining == 0 ? zlibConst.Z_SYNC_FLUSH : zlibConst.Z_NO_FLUSH);
                                if (err != zlibConst.Z_OK && err != zlibConst.Z_STREAM_END)
                                    throw new ZStreamException("deflating: " + outbound.msg);

                                int outCount = (int)(outbound.total_out - priorOut);
                                if (outCount > 0)
                                {
                                    if (headerBytes == 0)
                                    {
                                        tmp.Write(outBuffer, 0, outCount);
                                    }
                                    else
                                    {
                                        if (outCount < headerBytes)
                                        {
                                            throw new InvalidOperationException("Failed to write entire header");
                                        }
                                        // check the generated header meets our expectations
                                        // CMF is very specific - CM must be 8, and CINFO must be <=7 (for 32k window)
                                        if ((outBuffer[0] & 15) != 8)
                                        {
                                            throw new InvalidOperationException("Zlib CM header was incorrect");
                                        }
                                        if ((outBuffer[0] & 128) != 0) // if msb set, is > 7 - invalid
                                        {
                                            throw new InvalidOperationException("Zlib CINFO header was incorrect");
                                        }

                                        // FLG is less important; FCHECK is irrelevent, FLEVEL doesn't matter; but
                                        // FDICT must be zero, to ensure that we aren't expecting a an initialization dictionary
                                        if ((outBuffer[1] & 32) != 0)
                                        {
                                            throw new InvalidOperationException("Zlib FLG.FDICT header was set (must not be)");
                                        }

                                        // skip the header, and write anything else
                                        outCount -= headerBytes;
                                        if (outCount > 0)
                                        {
                                            tmp.Write(outBuffer, headerBytes, outCount);
                                        }
                                        headerBytes = 0; // all written now
                                    }
                                }
                            } while (outbound.avail_in > 0 || outbound.avail_out == 0);
                        }
                        if (remaining != 0) throw new EndOfStreamException();
                        if (headerBytes != 0) throw new InvalidOperationException("Zlib header was not written");

                        // verify the last 4 bytes, then drop them
                        tmp.Position = tmp.Length - 4;
                        NetContext.Fill(tmp, outBuffer, 4);
                        if (!(outBuffer[0] == 0x00 && outBuffer[1] == 0x00 && outBuffer[2] == 0xFF && outBuffer[3] == 0xFF))
                        {
                            throw new InvalidOperationException("expectation failed: 0000FFFF in the tail");
                        }

                        if (parent.disableContextTakeover && tmp.Length >= frame.PayloadLength)
                        { // compressing it didn't do anything useful; since we're going to discard
                          // the compression context, we might as well stick with the original data
                            payload.Position = 0;
                            payload = null; // so that it doesn't get disposed
                        }
                        else
                        {
                            // set our final output
                            tmp.Position = 0;
                            tmp.SetLength(tmp.Length - 4);
                            long bytesSaved = frame.PayloadLength - tmp.Length;
                            frame.Payload = tmp;
                            frame.PayloadLength = (int)tmp.Length;
                            frame.Reserved1 = true;
                            tmp = payload as BufferStream;
                            parent.RegisterOutboundBytesSaved(bytesSaved);
                        }
                    }
#if DEBUG
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        throw;
                    }
#endif
                    finally
                    {
                        if (outbound != null)
                        {
                            outbound.next_out = null;
                            outbound.next_in = null;
                        }
                        if (tmp != null) tmp.Dispose();
                        if (inBuffer != null) context.Recycle(inBuffer);
                        if (outBuffer != null) context.Recycle(outBuffer);
                        if (parent.disableContextTakeover) ClearContext(false, true);
                    }
                }
                yield return frame;
            }

            void ClearContext(bool @in, bool @out)
            {
                if (@in && inbound != null) { inbound.free(); inbound = null; }
                if (@out && outbound != null) { outbound.free(); outbound = null; }
            }
            void IDisposable.Dispose()
            {
                ClearContext(true, true);
            }
        }
    }
}
