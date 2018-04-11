using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace StackExchange.NetGain.WebSockets
{
    public class WebSocketClientFactory : IProtocolFactory
    {
        private static readonly WebSocketClientFactory @default = new WebSocketClientFactory();
        private static readonly WebSocketClientFactory hixie76 = new WebSocketClientFactory_Hixie76_00();
        public static WebSocketClientFactory Default { get { return @default; } }
        public static WebSocketClientFactory Hixie76 { get { return hixie76   ; } }
        protected WebSocketClientFactory() {}
        public virtual IProtocolProcessor GetProcessor()
        {
            return new WebSocketClientProcessor();
        }

        Connection IProtocolFactory.CreateConnection(EndPoint endpoint)
        {
            return (WebSocketConnection)CreateConnection() ?? new WebSocketConnection(endpoint);
        }
        protected virtual WebSocketConnection CreateConnection()
        {
            return null;
        }

        private class WebSocketClientFactory_Hixie76_00 : WebSocketClientFactory
        {
            public override IProtocolProcessor GetProcessor()
            {
                return new WebSocketClientProcessor_Hixie76_00();
            }
        }
    }
    internal class WebSocketClientProcessor_Hixie76_00 : WebSocketsSelectorBase
    {
        
        static string CreateRandomKey()
        {
            byte[] seed = new byte[4];
            NetContext.GetRandomBytes(seed);
            Random rand = new Random(BitConverter.ToInt32(seed, 0));

            // 16.  Let /spaces_1/ be a random integer from 1 to 12 inclusive.
            int spaces = rand.Next(1, 13);

            // 17.  Let /max_1/ be the largest integer not greater than
            // 4,294,967,295 divided by /spaces_1/.
            int max = int.MaxValue / spaces; // note using int.MaxValue for convenience

            //    18.  Let /number_1/ be a random integer from 0 to /max_1/ inclusive.
            int number = rand.Next(0, max);

            //19.  Let /product_1/ be the result of multiplying /number_1/ and
            // /spaces_1/ together.
            ulong product = (ulong)number * (ulong)spaces;

            //20.  Let /key_1/ be a string consisting of /product_1/, expressed in
            //base ten using the numerals in the range U+0030 DIGIT ZERO (0)
            //to U+0039 DIGIT NINE (9).
            var key = new StringBuilder().Append(product);

            //21.  Insert between one and twelve random characters from the ranges
            //U+0021 to U+002F and U+003A to U+007E into /key_1/ at random
            //positions.
            int charsToAdd = rand.Next(1, 13);
            for(int i = 0 ; i < charsToAdd ; i++)
            {
                int val = rand.Next(82);
                char c = (char) (val + val < 14 ? 0x21 : 0x3a);
                key.Insert(rand.Next(key.Length), c);
            }

            // 22.  Insert /spaces_1/ U+0020 SPACE characters into /key_1/ at random
            // positions other than the start or end of the string.
            for(int i = 0 ; i < spaces ; i++ )
            {
                key.Insert(rand.Next(1, key.Length - 1), ' ');
            }

            return key.ToString();
        }
        protected override void InitializeClientHandshake(NetContext context, Connection conn)
        {
            var connection = (WebSocketConnection) conn;
            if (string.IsNullOrEmpty(connection.Host)) throw new InvalidOperationException("Host must be specified");
            if (string.IsNullOrEmpty(connection.RequestLine)) throw new InvalidOperationException("RequestLine must be specified");

            string key1 = CreateRandomKey(), key2 = CreateRandomKey();
            byte[] key3 = new byte[8];
            NetContext.GetRandomBytes(key3);

            StringBuilder req = new StringBuilder(connection.RequestLine).Append("\r\n" +
                                    "Upgrade: WebSocket\r\n" + // note casing!
                                    "Connection: Upgrade\r\n" +
                                    "Sec-WebSocket-Key1: ").Append(key1).Append("\r\n" +
                                    "Sec-WebSocket-Key2: ").Append(key2).Append("\r\n" +
                                    "Host: ").Append(connection.Host).Append("\r\n");
            if (!string.IsNullOrEmpty(connection.Origin))
                req.Append("Origin: ").Append(connection.Origin).Append("\r\n");
            if (!string.IsNullOrEmpty(connection.Protocol))
                req.Append("Sec-WebSocket-Protocol: ").Append(connection.Protocol).Append("\r\n");
            req.Append("\r\n");




            expectedSecurityResponse = WebSocketsProcessor_Hixie76_00.ComputeKey(key1, key2, key3);
            EnqueueFrame(context, new StringFrame(req.ToString(), Encoding.ASCII, false));
            EnqueueFrame(context, new BinaryFrame(key3));
            connection.PromptToSend(context);
        }
        private Queue<object> pending;
        protected override void Send(NetContext context, Connection connection, object message)
        {
            lock (this)
            {
                if (pending == null) pending = new Queue<object>();
                pending.Enqueue(message);
            }
        }

        private byte[] expectedSecurityResponse;

        protected override int ProcessHeadersAndUpgrade(NetContext context, Connection connection, Stream input, Stream additionalData, int additionalOffset)
        {
            string requestLine;
            var headers = ParseHeaders(input, out requestLine);
            if (!string.Equals(headers["Upgrade"], "WebSocket", StringComparison.InvariantCultureIgnoreCase)
                || !string.Equals(headers["Connection"], "Upgrade", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new InvalidOperationException();
            }
            
            lock (this)
            {
                var protocol = new WebSocketsProcessor_Hixie76_00.Client(expectedSecurityResponse);
                expectedSecurityResponse = null; // use once only
                connection.SetProtocol(protocol);
                if (pending != null)
                {
                    while (pending.Count != 0)
                    {
                        connection.Send(context, pending.Dequeue());
                    }
                }
            }
            return 0;
        }
    }
    
    public class WebSocketClientProcessor : WebSocketsSelectorBase
    {
        private string expected;
        protected override void InitializeClientHandshake(NetContext context, Connection conn)
        {
            var connection = (WebSocketConnection) conn;
            byte[] nonce = new byte[16];
            NetContext.GetRandomBytes(nonce);
            var outgoing = Convert.ToBase64String(nonce);
            expected = WebSocketsProcessor_RFC6455_13.ComputeReply(context, outgoing);

            if(string.IsNullOrEmpty(connection.Host)) throw new InvalidOperationException("Host must be specified");
            if (string.IsNullOrEmpty(connection.RequestLine)) throw new InvalidOperationException("RequestLine must be specified");
            
            StringBuilder req = new StringBuilder(connection.RequestLine).Append("\r\n" +
                                    "Upgrade: websocket\r\n" +
                                    "Connection: Upgrade\r\n" +
                                    "Sec-WebSocket-Version: 13\r\n" +
                                    "Sec-WebSocket-Key: ").Append(outgoing).Append("\r\n" +
                                    "Host: ").Append(connection.Host).Append("\r\n");
            if (!string.IsNullOrEmpty(connection.Origin))
                req.Append("Origin: ").Append(connection.Origin).Append("\r\n");
            if (!string.IsNullOrEmpty(connection.Protocol))
                req.Append("Sec-WebSocket-Protocol: ").Append(connection.Protocol).Append("\r\n");
            var extnArray = context.Extensions;
            if (extnArray != null && extnArray.Length != 0)
            {
                List<string> extnHeaders = null;
                for (int i = 0; i < extnArray.Length; i++)
                {
                    var extn = extnArray[i] as IExtensionFactory;
                    if (extn != null)
                    {
                        string extnHeader = extn.GetRequestHeader();
                        if (!string.IsNullOrEmpty(extnHeader))
                        {
                            if (extnHeaders == null) extnHeaders = new List<string>();
                            extnHeaders.Add(extnHeader);
                        }
                    }
                }
                if (extnHeaders != null)
                {
                    req.Append("Sec-WebSocket-Extensions: ").Append(extnHeaders[0]);
                    for (int i = 1; i < extnHeaders.Count; i++)
                        req.Append(", ").Append(extnHeaders[i]);
                    req.Append("\r\n");
                }
            }

            req.Append("\r\n");
            EnqueueFrame(context, new StringFrame(req.ToString(), Encoding.ASCII));
            connection.PromptToSend(context);
        }

        
        protected override int ProcessHeadersAndUpgrade(NetContext context, Connection connection, Stream input, Stream additionalData, int additionalOffset)
        {
            string requestLine;
            var headers = ParseHeaders(input, out requestLine);
            if (!string.Equals(headers["Upgrade"], "WebSocket", StringComparison.InvariantCultureIgnoreCase)
                || !string.Equals(headers["Connection"], "Upgrade", StringComparison.InvariantCultureIgnoreCase)
                || headers["Sec-WebSocket-Accept"] != expected)
            {
                throw new InvalidOperationException();
            }

            lock (this)
            {
                AddMatchingExtensions(headers, connection, context.Extensions);
                var protocol = new WebSocketsProcessor_RFC6455_13(true);
                connection.SetProtocol(protocol);
                if (pending != null)
                {
                    while (pending.Count != 0)
                    {
                        connection.Send(context, pending.Dequeue());
                    }
                }
            }
            return 0;
        }

        private Queue<object> pending;
        protected override void Send(NetContext context, Connection connection, object message)
        {
            lock (this)
            {
                if (pending == null) pending = new Queue<object>();
                pending.Enqueue(message);
            }
        }
    }
}
