using System.Collections.Specialized;
using System.IO;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.NetGain.WebSockets
{
    internal class WebSocketsProcessor_RFC6455_13 : WebSocketsProcessor
    {
        public override string ToString()
        {
            return "rfc6455";
        }
        private readonly bool isClient;
        public WebSocketsProcessor_RFC6455_13(bool isClient)
        {
            this.isClient = isClient;
        }
        public bool IsClient { get { return isClient; } }
        
        public static int Copy(Stream source, Stream destination, byte[] buffer, int? mask, int count)
        {
            int totalRead = 0, read;
            if(mask == null)
            {
                while (count > 0 && (read = source.Read(buffer, 0, Math.Min(count, buffer.Length))) > 0)
                {
                    destination.Write(buffer, 0, read);
                    totalRead += read;
                    count -= read;
                }
            } else
            {
                int effectiveLength = (buffer.Length/8)*8; // need nice sized! e.g. 100-byte array, can only use 96 bytes
                if(effectiveLength == 0) throw new ArgumentException("buffer is too small to be useful", "buffer");

                // we read it big-endian, so we need to *write* it big-endian, and then we'll use unsafe code to read the value,
                // so that the endian-ness we use is CPU-endian, and so it matches whatever we do below
                int maskValue = mask.Value;
                buffer[0] = buffer[4] = (byte) (maskValue >> 24);
                buffer[1] = buffer[5] = (byte) (maskValue >> 16);
                buffer[2] = buffer[6] = (byte) (maskValue >> 8);
                buffer[3] = buffer[7] = (byte) maskValue;
                unsafe
                {
                    
                    fixed (byte* bufferPtr = buffer) // treat the byte-array as a pile-of-ulongs
                    {
                        var longPtr = (ulong*)bufferPtr;
                        ulong xorMask = *longPtr;
                        int bytesThisIteration;
                        do
                        {
                            // now, need to fill buffer as much as possible each time, as we want to work in exact
                            // units of 8 bytes; we don't need to worry about applying the mask to any garbage at the
                            // end of the buffer, as we simply won't copy that out
                            int offset = 0, available = effectiveLength;
                            while (available > 0 && (read = source.Read(buffer, offset, Math.Min(count, available))) > 0)
                            {
                                available -= read;
                                totalRead += read;
                                count -= read;
                                offset += read;
                            }
                            bytesThisIteration = effectiveLength - available;
                            int chunks = bytesThisIteration/8;
                            if ((available%8) != 0) chunks++;

                            // apply xor 8-bytes at a time, coz we haz 64-bit CPU, baby!
                            for (int i = 0; i < chunks; i++)
                                longPtr[i] ^= xorMask;

                            destination.Write(buffer, 0, bytesThisIteration);
                        } while (bytesThisIteration != 0);
                    }
                }
            }
            if(count != 0) throw new EndOfStreamException();
            return totalRead;
        }



        protected override void InitializeClientHandshake(NetContext context, Connection connection)
        {
            throw new System.NotImplementedException();
        }

        public const int MaxSupportedVersion = 13;

        protected override int ProcessIncoming(NetContext context, Connection connection, Stream incoming)
        {
            if (incoming.Length < 2) return 0; // can't read that; frame takes at minimum two bytes

            

            byte[] buffer = null;
            try
            {
                buffer = context.GetBuffer();
                int read;
                // read as much as possible up to 14 bytes; that gives us space for 2 bytes frame header, 8 bytes length, and 4 bytes mask
                read = NetContext.TryFill(incoming, buffer, 14);

                int headerLength;
                var frame = WebSocketsFrame.TryParseFrameHeader(buffer, read, out headerLength);

                if (frame == null) return 0;
                int payloadLen = frame.PayloadLength;

#if VERBOSE
                Debug.WriteLine("Parsed header from: " + BitConverter.ToString(buffer, 0, headerLength));
#endif

                if (incoming.Length < headerLength + payloadLen) return 0; // not enough data to read the payload

                if (payloadLen != 0)
                {
                    Stream payload = null;
                    try
                    {
                        payload = new BufferStream(context, context.Handler.MaxIncomingQuota);
                        if (read != headerLength) incoming.Position -= (read - headerLength); // we got it wrong...

                        Copy(incoming, payload, buffer, frame.Mask, payloadLen);

                        if (payloadLen != payload.Length)
                        {
                            throw new InvalidOperationException("I munged the data");
                        }
                        frame.Payload = payload;
                        payload = null;
                    }
                    finally
                    {
                        if (payload != null) payload.Dispose();
                    }
                }
                foreach (var final in ApplyExtensions(context, connection, frame, true))
                {
                    ProcessFrame(context, connection, final);
                }
                return headerLength + payloadLen;
            }
            finally
            {
                context.Recycle(buffer);
            }
        }
        protected override void GracefulShutdown(NetContext context, Connection connection)
        {
            connection.DisableRead();
            var ws = (WebSocketConnection) connection;
            if(!ws.HasSentClose)
            {
                ws.HasSentClose = true;
                var close = new WebSocketsFrame();
                if (IsClient) close.Mask = CreateMask();
                close.OpCode = WebSocketsFrame.OpCodes.Close;
                close.PayloadLength = 0;
                SendControl(context, close);
            }
            SendShutdown(context);
            connection.PromptToSend(context);
        }
        private void ProcessFrame(NetContext context, Connection connection, WebSocketsFrame frame)
        {
            if(IsClient)
            {
                if(frame.Mask.HasValue) throw new InvalidOperationException("Clients only expect unmasked frames");
            } else
            {
                if (!frame.Mask.HasValue) throw new InvalidOperationException("Servers only expect masked frames");
            }
            var ws = (WebSocketConnection) connection;
            if(frame.IsControlFrame)
            {
                if(!frame.IsFinal) throw new InvalidOperationException("control frames cannot be fragmented");
                switch(frame.OpCode)
                {
                    case WebSocketsFrame.OpCodes.Close:
                        // respond with a closing handshake
                        GracefulShutdown(context, connection);
                        break;
                    case WebSocketsFrame.OpCodes.Pong:
                        // could add some reaction here
                        break;
                    case WebSocketsFrame.OpCodes.Ping:
                        // send a "pong" with the same payload
                        var pong = new WebSocketsFrame();
                        if (IsClient) pong.Mask = CreateMask();
                        pong.OpCode = WebSocketsFrame.OpCodes.Pong;
                        pong.PayloadLength = frame.PayloadLength;
                        if(frame.Payload != null)
                        {
                            var ms = new MemoryStream((int)frame.Payload.Length);
                            byte[] buffer = null;
                            try
                            {
                                buffer = context.GetBuffer();
                                Copy(frame.Payload, ms, buffer, null, frame.PayloadLength);
                            } finally
                            {
                                context.Recycle(buffer);
                            }
                            pong.Payload = ms;
                        }
                        SendControl(context, pong);
                        connection.PromptToSend(context);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
            else
            {
                if (!ws.AllowIncomingDataFrames) throw new InvalidOperationException("Data frames disabled");

                int pendingCount = FrameCount;
                if (frame.OpCode == WebSocketsFrame.OpCodes.Continuation)
                {
                    if(pendingCount == 0) throw new InvalidOperationException("Continuation of nothing");
                }
                else  if (pendingCount != 0)
                {
                    throw new InvalidOperationException("Should be a continuation");
                }
                if (!frame.IsFinal)
                {
                    Push(context, frame);
                }
                else
                {
                    if(pendingCount == 0)
                    {
                        Process(context, connection, frame.Payload, frame.OpCode);
                    } else
                    {
                        throw new NotImplementedException();
                    }
                }

            }
            if (frame.OpCode == WebSocketsFrame.OpCodes.Close)
            {
                Debug.WriteLine("Sent close frame: " + connection);
            }
        }
        private void Process(NetContext context, Connection connection, Stream stream, WebSocketsFrame.OpCodes opCode)
        {
            using (stream)
            {
                switch (opCode)
                {
                    case WebSocketsFrame.OpCodes.Text:
                        string s;
                        if (stream == null || stream.Length == 0) s = "";
                        else
                        {
                            stream.Position = 0;
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                s = reader.ReadToEnd();
                            }
                        }
                        context.Handler.OnReceived(connection, s);
                        break;
                    case WebSocketsFrame.OpCodes.Binary:
                        byte[] blob;
                        int len;
                        if (stream == null || (len = ((int)stream.Length)) == 0) blob = new byte[0]; 
                        else
                        {
                            blob = new byte[len];
                            int offset = 0, read;
                            stream.Position = 0;
                            while((read = stream.Read(blob, offset, len)) > 0)
                            {
                                offset += read;
                                len -= read;
                            }
                            if(len != 0) throw new EndOfStreamException();
                        }
                        context.Handler.OnReceived(connection, blob);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }
        
        static int CreateMask()
        {
            byte[] mask = new byte[4];
            NetContext.GetRandomBytes(mask); 
            return BitConverter.ToInt32(mask, 0);
        }

        public override void Send(NetContext context, Connection connection, object message)
        {
            if(message == null) throw new ArgumentNullException("message");

            string s;
            WebSocketConnection wsConnection;
            WebSocketsFrame frame;

            var encoding = Encoding.UTF8;
            if ((s = message as string) != null && (wsConnection = connection as WebSocketConnection) != null
                && wsConnection.MaxCharactersPerFrame > 0 && s.Length > wsConnection.MaxCharactersPerFrame)
            {
                int remaining = s.Length, charIndex = 0;
                bool isFirst = true;
                char[] charBuffer = s.ToCharArray();
                var batch = new List<IFrame>();
                while(remaining > 0)
                {
                    int charCount = Math.Min(remaining, wsConnection.MaxCharactersPerFrame);
                    var buffer = encoding.GetBytes(charBuffer, charIndex, charCount);
                    //Debug.WriteLine("> " + new string(charBuffer, charIndex, charCount));
                    remaining -= charCount;
                    charIndex += charCount;

                    frame = new WebSocketsFrame();
                    frame.OpCode = isFirst ? WebSocketsFrame.OpCodes.Text : WebSocketsFrame.OpCodes.Continuation;
                    isFirst = false;
                    frame.Payload = new BufferStream(context, context.Handler.MaxOutgoingQuota);
                    frame.Payload.Write(buffer, 0, buffer.Length);
                    frame.PayloadLength = buffer.Length;
                    frame.Mask = IsClient ? (int?)CreateMask() : null;
                    frame.Payload.Position = 0;
                    frame.IsFinal = remaining <= 0;
                    foreach (var final in ApplyExtensions(context, connection, frame, false))
                    {
                        batch.Add(final);
                    }                    
                }
                SendData(context, batch);
                return;
            }            


            byte[] blob;
            frame = new WebSocketsFrame();
            frame.Payload = new BufferStream(context, context.Handler.MaxOutgoingQuota);
            if(s != null)
            {
                frame.OpCode = WebSocketsFrame.OpCodes.Text;
                var buffer = encoding.GetBytes(s);
                //Debug.WriteLine("> " + s);
                frame.Payload.Write(buffer, 0, buffer.Length);
                frame.PayloadLength = buffer.Length;
            }
            else if ((blob = message as byte[]) != null)
            {
                frame.OpCode = WebSocketsFrame.OpCodes.Binary;
                frame.Payload.Write(blob, 0, blob.Length);
                frame.PayloadLength = blob.Length;
            }
            else
            {
                throw new NotSupportedException(message.ToString());
            }
            frame.Mask = IsClient ? (int?)CreateMask() : null;
            frame.Payload.Position = 0;
            frame.IsFinal = true;

            foreach(var final in ApplyExtensions(context, connection, frame, false))
            {
                SendData(context, final);
            }
        }

        private static IEnumerable<WebSocketsFrame> ApplyExtensions(NetContext context, Connection connection, WebSocketsFrame frame, bool inbound)
        {
            var wsc = connection as WebSocketConnection;
            var extn = wsc == null ? null : wsc.Extensions;

            IEnumerable<WebSocketsFrame> frames = Enumerable.Repeat(frame, 1);

            if (extn != null && extn.Length != 0)
            {
                for (int i = 0; i < extn.Length; i++)
                {
                    var actualExtension = extn[i];
                    if (inbound)
                    {
                        frames = frames.SelectMany(x => actualExtension.ApplyIncoming(context, wsc, x));
                    }
                    else
                    {
                        frames = frames.SelectMany(x => actualExtension.ApplyOutgoing(context, wsc, x));
                    }
                }
            }
            return frames;
        }

        protected internal override void CompleteHandshake(NetContext context, Connection connection, Stream input, string requestLine, StringDictionary headers, byte[] body)
        {


            string key = headers["Sec-WebSocket-Key"];
            if(key != null) key = key.Trim();
            string response = ComputeReply(context, key);

            var builder = new StringBuilder(
                              "HTTP/1.1 101 Switching Protocols\r\n"
                            + "Upgrade: websocket\r\n"
                            + "Connection: Upgrade\r\n"
                            + "Sec-WebSocket-Accept: ").Append(response).Append("\r\n");

            var wc = connection as WebSocketConnection;
            var extn = wc == null ? null : wc.Extensions;
            if (extn != null)
            {
                List<string> extnHeaders = null;
                for (int i = 0; i < extn.Length; i++)
                {
                    string extnHeader = extn[i].GetResponseHeader();
                    if (!string.IsNullOrEmpty(extnHeader))
                    {
                        if (extnHeaders == null) extnHeaders = new List<string>();
                        extnHeaders.Add(extnHeader);
                    }
                }
                if (extnHeaders != null)
                {
                    builder.Append("Sec-WebSocket-Extensions: ").Append(extnHeaders[0]);
                    for (int i = 1; i < extnHeaders.Count; i++)
                        builder.Append(", ").Append(extnHeaders[i]);
                    builder.Append("\r\n");
                }
            }

            builder.Append("\r\n");
            SendControl(context, new StringFrame(builder.ToString(), Encoding.ASCII));
            connection.PromptToSend(context);
        }

        internal static string ComputeReply(NetContext context, string webSocketKey)
        {
            //To prove that the handshake was received, the server has to take two
            //pieces of information and combine them to form a response.  The first
            //piece of information comes from the |Sec-WebSocket-Key| header field
            //in the client handshake:

            //     Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==

            //For this header field, the server has to take the value (as present
            //in the header field, e.g., the base64-encoded [RFC4648] version minus
            //any leading and trailing whitespace) and concatenate this with the
            //Globally Unique Identifier (GUID, [RFC4122]) "258EAFA5-E914-47DA-
            //95CA-C5AB0DC85B11" in string form, which is unlikely to be used by
            //network endpoints that do not understand the WebSocket Protocol.  A
            //SHA-1 hash (160 bits) [FIPS.180-3], base64-encoded (see Section 4 of
            //[RFC4648]), of this concatenation is then returned in the server's
            //handshake.
            if (string.IsNullOrEmpty(webSocketKey) || webSocketKey.Length != 24) throw new ArgumentException("webSocketKey");
            
            const string WebSocketKeySuffix = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] buffer = null;
            try
            {
                buffer = context.GetBuffer();
                int offset = Encoding.ASCII.GetBytes(webSocketKey, 0, webSocketKey.Length, buffer, 0);
                int len = offset + Encoding.ASCII.GetBytes(WebSocketKeySuffix, 0, WebSocketKeySuffix.Length, buffer, offset);
                using(var sha = SHA1.Create())
                {
                    return Convert.ToBase64String(sha.ComputeHash(buffer, 0, len));
                }
            }
            finally
            {
                context.Recycle(buffer);
            }
        }
    }
}
