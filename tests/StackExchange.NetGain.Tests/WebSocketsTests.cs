using NUnit.Framework;
using StackExchange.NetGain;
using StackExchange.NetGain.WebSockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.NetGain.Tests
{
    [TestFixture]
    public class WebSocketsTests : WebSocketsMessageProcessor
    {
        class CustomFactory : WebSocketsSelectorProcessor
        {
            protected override bool TryBasicResponse(NetContext context, System.Collections.Specialized.StringDictionary requestHeaders, string requestLine, System.Collections.Specialized.StringDictionary responseHeaders, out HttpStatusCode code, out string body)
            {
                var match = Regex.Match(requestLine, @"GET (.*) HTTP/1.[01]");
                Uri uri;
                if (match.Success && Uri.TryCreate(match.Groups[1].Value.Trim(), UriKind.RelativeOrAbsolute, out uri))
                {
                    switch (uri.OriginalString)
                    {
                        case "/ping":
                            code = System.Net.HttpStatusCode.OK;
                            body = "Ping response from custom factory";
                            return true;
                    }
                }
                return base.TryBasicResponse(context, requestHeaders, requestLine, responseHeaders, out code, out body);
            }
        }
        TcpServer server;

        protected override void OnReceive(WebSocketConnection connection, string message)
        {
            var chars = message.ToCharArray();
            Array.Reverse(chars);
            string s = new string(chars);
            Send(connection, s);
        }

        [TestFixtureSetUp]
        public void Setup()
        {
            server = new TcpServer();
            //server.Extensions = new object[] { new PerFrameDeflate(0, false) };
            server.MessageProcessor = this;
            server.ProtocolFactory = new CustomFactory();
            server.Start("test", new IPEndPoint(IPAddress.Loopback, 20000));
        }

        [Test]
        public void RespondsToBasicHttp()
        {
            using (var client = new WebClient())
            {
                string s = client.DownloadString("http://127.0.0.1:20000/ping");
                Assert.AreEqual("Ping response from custom factory\r\n", s);
            }
        }

        [Test]
        public void RespondsToRFC6455()
        {
            using (var client = new TcpClient())
            {
                client.ProtocolFactory = WebSocketClientFactory.Default;
                client.Open(new IPEndPoint(IPAddress.Loopback, 20000));
                string resp = (string)client.ExecuteSync("abcdefg");
                Assert.AreEqual("gfedcba", resp);
            }
        }

        [Test]
        public void RespondsToRFC6455_WithDeflate()
        {
            using (var client = new TcpClient())
            {
                //client.Extensions = new object[] { new PerFrameDeflate(0, false) };
                client.ProtocolFactory = WebSocketClientFactory.Default;
                client.Open(new IPEndPoint(IPAddress.Loopback, 20000));
                string resp = (string)client.ExecuteSync("abcdefg");
                Assert.AreEqual("gfedcba", resp);
            }
        }
        [Test]
        public void RespondsToHixie76()
        {
            using (var client = new TcpClient())
            {
                client.ProtocolFactory = WebSocketClientFactory.Hixie76;
                client.Open(new IPEndPoint(IPAddress.Loopback, 20000));
                string resp = (string)client.ExecuteSync("abcdefg");
                Assert.AreEqual("gfedcba", resp);
            }
        }

        [Test]
        public void ResurrectsListeners()
        {
            using (var client = new TcpClient())
            {
                client.ProtocolFactory = WebSocketClientFactory.Hixie76;
                client.Open(new IPEndPoint(IPAddress.Loopback, 20000));
                string resp = (string)client.ExecuteSync("abcdefg");
                Assert.AreEqual("gfedcba", resp);
            }

            server.ImmediateReconnectListeners = false;
            server.KillAllListeners();
            Thread.Sleep(500);
            server.Heartbeat();
            Thread.Sleep(500); // give it time to spin up!

            using (var client = new TcpClient())
            {
                client.ProtocolFactory = WebSocketClientFactory.Hixie76;
                client.Open(new IPEndPoint(IPAddress.Loopback, 20000));
                string resp = (string)client.ExecuteSync("abcdefgh");
                Assert.AreEqual("hgfedcba", resp);
            }
        }

        [Test]
        public void RespondToMicrosoftWebSocketClient()
        {
            using(var socket = new ClientWebSocket())
            {
                socket.ConnectAsync(new Uri("ws://127.0.0.1:20000/"), CancellationToken.None).Wait(1000);
                var buffer = Encoding.UTF8.GetBytes("abcdefg");
                socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None).Wait(1000);
                var receiveBuffer = ClientWebSocket.CreateClientBuffer(1024, 1024);
                var result = socket.ReceiveAsync(receiveBuffer, CancellationToken.None);
                result.Wait(1000);
                var msg = Encoding.UTF8.GetString(receiveBuffer.Array, 0, result.Result.Count);
                Assert.AreEqual("gfedcba", msg);
            }            
        }

        [TestFixtureTearDown]
        public void Teardown()
        {
            using (server)
            {
                server.Stop();
            }
        }

    }
}
