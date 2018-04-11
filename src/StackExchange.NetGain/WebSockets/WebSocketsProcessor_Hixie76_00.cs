
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StackExchange.NetGain.WebSockets
{
    internal abstract class WebSocketsProcessor_Hixie76_00 : WebSocketsProcessor
    {
        public override string ToString()
        {
            return "hixie-76";
        }
        private class HixieFrame : StringFrame
        {
            public HixieFrame(string value) : base(value, Encoding.UTF8)
            {
            }
            protected override void Write(NetContext context, Connection connection, Stream stream)
            {
                stream.WriteByte(0x00);
                base.Write(context, connection, stream);
                stream.WriteByte(0xFF);
            }
        }
        protected override void GracefulShutdown(NetContext context, Connection connection)
        {
            connection.DisableRead();
            var ws = (WebSocketConnection) connection;
            if(ws.HasSentClose)
            {
                ws.HasSentClose = true;
                SendData(context, new BinaryFrame(new byte[] {0xFF, 0x00}, true));
            }
            SendShutdown(context);
            connection.PromptToSend(context);
        }
        private static int ComputeKeyHash(string key)
        {
            long accumulator = 0;
            int spaces = 0;
            foreach(char c in key)
            {
                switch(c)
                {
                    case ' ':
                        spaces++;
                        break;
                    case '0': case '1': case '2': case '3':
                    case '4': case '5': case '6': case '7':
                    case '8': case '9':
                        accumulator = (accumulator*10) + (c - '0');
                        break;
                }
            }
            return (int) (accumulator/spaces);
        }
        public static byte[] ComputeKey(string key1, string key2, byte[] key3)
        {
            int hash1 = ComputeKeyHash(key1), hash2 = ComputeKeyHash(key2);
            byte[] result = new byte[8 + key3.Length];
            result[0] = (byte) (hash1 >> 24);
            result[1] = (byte) (hash1 >> 16);
            result[2] = (byte) (hash1 >> 8);
            result[3] = (byte) (hash1);
            result[4] = (byte)(hash2 >> 24);
            result[5] = (byte)(hash2 >> 16);
            result[6] = (byte)(hash2 >> 8);
            result[7] = (byte)(hash2);
            System.Buffer.BlockCopy(key3, 0, result, 8, key3.Length);
            using(var md5 = MD5.Create())
            {
                return md5.ComputeHash(result);
            }
        }
        
        protected override void InitializeClientHandshake(NetContext context, Connection connection)
        {
            throw new System.NotSupportedException();
        }

        protected internal override void CompleteHandshake(NetContext context, Connection connection, Stream input, string requestLine, System.Collections.Specialized.StringDictionary headers, byte[] body)
        {
            throw new NotSupportedException();
        }

        internal class Client : WebSocketsProcessor_Hixie76_00
        {
            private byte[] expectedResponse;

            public Client(byte[] expectedResponse)
            {
                this.expectedResponse = expectedResponse;
            }
            protected override int ProcessIncoming(NetContext context, Connection connection, Stream incoming)
            {
                if (expectedResponse != null)
                {
                    if (incoming.Length < expectedResponse.Length) return 0; // not enough to check the response

                    byte[] actual = new byte[expectedResponse.Length];
                    NetContext.Fill(incoming, actual, actual.Length);
                    for (int i = 0; i < actual.Length; i++)
                    {
                        if (actual[i] != expectedResponse[i]) throw new InvalidOperationException("Incorrect Hixie76 respoonse");
                    }
                    expectedResponse = null; // all verified now
                    return actual.Length; // the amount we processed

                }
                return base.ProcessIncoming(context, connection, incoming);
            } 
        }
        internal class Server : WebSocketsProcessor_Hixie76_00
        {
            Tuple<string, string> keys;
            protected override int ProcessIncoming(NetContext context, Connection connection, Stream incoming)
            {
                if (keys != null) // handshake is not yet complete
                {
                    if (incoming.Length < 8) return 0; // need 8 bytes to finish the handshake

                    var key3 = new byte[8];
                    NetContext.Fill(incoming, key3, 8);

                    // now that we're on a different protocol, send the server's response to the client's challenge
                    var securityCheck = ComputeKey(keys.Item1, keys.Item2, key3);
                    keys = null; // wipe
                    SendControl(context, new BinaryFrame(securityCheck));
                    connection.PromptToSend(context);
                    return 8;
                }
                return base.ProcessIncoming(context, connection, incoming);
            }
            protected internal override void CompleteHandshake(NetContext context, Connection connection, System.IO.Stream input, string requestLine, System.Collections.Specialized.StringDictionary headers, byte[] body)
            {
                keys = Tuple.Create(headers["Sec-WebSocket-Key1"], headers["Sec-WebSocket-Key2"]);
                var builder = new StringBuilder(
                    "HTTP/1.1 101 WebSocket Protocol Handshake\r\n"
                    + "Upgrade: WebSocket\r\n"
                    + "Connection: Upgrade\r\n");
                var conn = (WebSocketConnection)connection;
                if (!string.IsNullOrEmpty(conn.Host))
                { // we should add the origin/location, but that depends on what we have available to us
                    builder.Append("Sec-WebSocket-Origin: ").Append(conn.Origin).Append("\r\n");
                    if (!string.IsNullOrEmpty(conn.RequestLine))
                    {
                        Match match;
                        if ((match = Regex.Match(conn.RequestLine, @"GET ([^\s]+) HTTP")).Success)
                        {
                            builder.Append("Sec-WebSocket-Location: ").Append("ws://").Append(conn.Host).Append(match.Groups[1].Value).Append("\r\n");
                        }
                    }
                }
                if (!string.IsNullOrEmpty(conn.Protocol))
                    builder.Append("Sec-WebSocket-Protocol: ").Append(conn.Protocol).Append("\r\n");
                builder.Append("\r\n");
                SendControl(context, new StringFrame(builder.ToString(), Encoding.ASCII, false));
                connection.PromptToSend(context);
            }
        }
        
            

            
        protected override int ProcessIncoming(NetContext context, Connection connection, System.IO.Stream incoming)
        {
            if (incoming.Length < 2) return 0; // can't read that; start/end markers take at least 2 bytes

            switch(incoming.ReadByte())
            {
                case 0x00:
                    var ws = (WebSocketConnection) connection;
                    if(!ws.AllowIncomingDataFrames) throw new InvalidOperationException("Data frames disabled");
                    // data frame is 0x00 [data] 0xFF
                    int len = 0, cur;
                    while((cur = incoming.ReadByte()) != 0xFF && cur >= 0)
                    {
                        len++;
                    }
                    if(cur < 0) throw new EndOfStreamException();

                    incoming.Position = 1;
                    string value;
                    if (len == 0) value = "";
                    else if (len <= NetContext.BufferSize)
                    {
                        byte[] buffer = null;
                        try
                        {
                            buffer = context.GetBuffer();
                            NetContext.Fill(incoming, buffer, len);
                            value = Encoding.UTF8.GetString(buffer, 0, len);
                        } finally
                        {
                            context.Recycle(buffer);
                        }
                    } else
                    {
                        var buffer = new byte[len];
                        NetContext.Fill(incoming, buffer, len);
                        value = Encoding.UTF8.GetString(buffer, 0, len);
                    }
                    context.Handler.OnReceived(connection, value);
                    return len + 2;
                case 0xFF:
                    // shutdown is 0xFF 0x00
                    if(incoming.ReadByte() == 0x00)
                    {
                        GracefulShutdown(context, connection);
                        return 2;
                    } else
                    {
                        throw new InvalidOperationException("protocol fail");
                    }
                default:
                    throw new InvalidOperationException("protocol fail");
            }
        }
        public override void Send(NetContext context, Connection connection, object message)
        {
            SendData(context, new HixieFrame((string)message));
        }
    }
    
}
