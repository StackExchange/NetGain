
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using StackExchange.NetGain;
using StackExchange.NetGain.WebSockets;
using TcpClient = StackExchange.NetGain.TcpClient;
using System.Text.RegularExpressions;

namespace ConsoleApplication1
{
    class Program
    {
        
        static void Main()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 6002);
            using(var server = new TcpServer())
            {
                server.ProtocolFactory = WebSocketsSelectorProcessor.Default;
                server.ConnectionTimeoutSeconds = 60;
                server.Received += msg =>
                {
                    var conn = (WebSocketConnection)msg.Connection;
                    string reply = (string)msg.Value + " / " + conn.Host;
                    Console.WriteLine("[server] {0}", msg.Value);
                    msg.Connection.Send(msg.Context, reply);
                };
                server.Start("abc", endpoint);
                Console.WriteLine("Server running; press any key");
                Console.ReadKey();
            }
            Console.WriteLine("Server dead; press any key");
            Console.ReadKey();
        }
        static void Main2()
        {
            //var test = WebSocketsProcessor_Hixie76_00.ComputeKey("18x 6]8vM;54 *(5:  {   U1]8  z [  8", "1_ tx7X d  <  nw  334J702) 7]o}` 0",
            //    Encoding.ASCII.GetBytes("Tm[K T2u") );
            //var asc = Encoding.ASCII.GetString(test);
            //string s = "GET /demo HTTP/1.1";
            //var match = Regex.Match(s, @"GET ([^\s]+) HTTP");
            //if(match.Success)
            //{
            //    Console.WriteLine(match.Groups[1].Value);
            //}
            //var handler = new TcpHandler();
            //var evt = new ManualResetEvent(false);
            //handler.Received += msg =>
            //{
            //    Console.WriteLine(msg.Value);
            //    evt.Set();
            //};
            //var ctx = new NetContext(delegate {}, handler);
            //IProtocolProcessor proc = new WebSocketsProcessor_RFC6455_13(false);
            //IProtocolFactory factory = WebSocketsSelectorProcessor.Default;
            //var conn = factory.CreateConnection();
            //var ms = new MemoryStream(new byte[] {0x81, 0x85, 0x37, 0xfa, 0x21, 0x3d, 0x7f, 0x9f, 0x4d, 0x51, 0x58});
            //int i = proc.ProcessIncoming(ctx, conn, ms);
            //evt.WaitOne();

        }
        static void Main3() {

            IPEndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 6002);

            int count = 0;
            Stopwatch watch = null;
            const int ITEM_COUNT = 5, CLIENT_COUNT = 1;
            bool broadcast = false;
            Action<Message> callback = msg =>
            {
                int complete = Interlocked.Increment(ref count);
                if (broadcast)
                {
                    if(complete == CLIENT_COUNT)
                    {
                        watch.Stop();
                        Console.WriteLine(msg.Value);
                        Console.WriteLine("{0} x {1}, {2}ms, {3}μs/op, {4}op/s", 1, CLIENT_COUNT,
                                          watch.ElapsedMilliseconds,
                                          (watch.ElapsedMilliseconds * 1000) / (CLIENT_COUNT),
                                          watch.ElapsedMilliseconds == 0
                                              ? -1
                                              : ((CLIENT_COUNT * 1000) / watch.ElapsedMilliseconds));
                    }
                    
                }
                else
                {
                    if ((complete % 1000) == 0) Console.WriteLine(complete);
                    if (complete == ITEM_COUNT * CLIENT_COUNT)
                    {
                        watch.Stop();
                        Console.WriteLine(msg.Value);
                        Console.WriteLine("{0} x {1}, {2}ms, {3}μs/op, {4}op/s", ITEM_COUNT, CLIENT_COUNT,
                                          watch.ElapsedMilliseconds,
                                          (watch.ElapsedMilliseconds * 1000) / (ITEM_COUNT * CLIENT_COUNT),
                                          watch.ElapsedMilliseconds == 0
                                              ? -1
                                              : ((ITEM_COUNT * CLIENT_COUNT * 1000) / watch.ElapsedMilliseconds));
                    }
                }
            };

            using (var server = new TcpServer())
            {
                server.ProtocolFactory = WebSocketsSelectorProcessor.Default;
                server.Received += msg =>
                {
                    var conn = (WebSocketConnection) msg.Connection;
                    string reply = (string) msg.Value + " / " + conn.Host;
                    Console.WriteLine("[server] {0}", msg.Value);
                    msg.Connection.Send(msg.Context, reply);
                };
                server.Start("abc", endpoint);
                
                var legacy = new TcpClient();
                legacy.ProtocolFactory = WebSocketClientFactory.Hixie76;
                legacy.Received += msg =>
                {
                    Console.WriteLine("[legacy client]: {0}", msg.Value);
                };
                legacy.Open(endpoint, c =>
                {
                    var conn = (WebSocketConnection) c;
                    conn.RequestLine = "GET /chat HTTP/1.1";
                    conn.Host = "stackoverflow.com";
                });

                var rfc = new TcpClient();
                rfc.ProtocolFactory = WebSocketClientFactory.Default;
                rfc.Received += msg =>
                {
                    Console.WriteLine("[rfc client]: {0}", msg.Value);
                };
                rfc.Open(endpoint, c =>
                {
                    var conn = (WebSocketConnection)c;
                    conn.RequestLine = "GET /chat HTTP/1.1";
                    conn.Host = "stackoverflow.com";
                });

                Console.WriteLine("press to test");
                Console.ReadLine();

                legacy.Execute("hello", false);
                rfc.Execute("world", false);
                //server.Broadcast("boo");
                /*
                List<TcpClient> clients = new List<TcpClient>();
                for (int i = 0; i < CLIENT_COUNT; i++)
                {
                    var client = new TcpClient();
                    clients.Add(client);
                    client.ProtocolFactory = WebSocketClientFactory.Default;
                    client.Received += callback;
                    client.Open(endpoint, c =>
                    {
                        var conn = (WebSocketConnection) c;
                        conn.RequestLine = "GET /chat HTTP/1.1";
                        conn.Host = "stackoverflow.com";
                    });
                }
                Console.WriteLine("All opened");
                Thread.Sleep(1500);

                Console.WriteLine("Writing...");
                watch = Stopwatch.StartNew();
                foreach(var client in clients)
                {
                    for (int j = 0; j < ITEM_COUNT; j++)
                    {
                        client.Send("Testing");
                    }
                }
                Console.WriteLine("Press any key to broadcast");
                Console.ReadKey();
                broadcast = true;
                count = 0;
                watch = Stopwatch.StartNew();
                server.Broadcast("Boo!");


                Console.WriteLine("Press any key to broadcast");
                Console.ReadKey();
                count = 0;
                watch = Stopwatch.StartNew();
                server.Broadcast("Boo!");
                */
                Console.WriteLine("Press any key to end");
                Console.ReadKey();
            }
        }
    }
    //class EchoFactory : IProtocolFactory
    //{
    //    public IProtocolProcessor GetProcessor()
    //    {
    //        return new Echo();
    //    }
    //    public Connection CreateConnection()
    //    {
    //        return null;
    //    }
    //    class Echo : ProtocolProcessor
    //    {

    //        protected override int ProcessIncoming(NetContext context, Connection connection, Stream incomingBuffer)
    //        {
    //            using(var ms = new MemoryStream((int)incomingBuffer.Length))
    //            {
    //                var buffer = context.GetBuffer();
    //                int read, totalBytes = 0;
    //                while((read = incomingBuffer.Read(buffer, 0, buffer.Length)) > 0)
    //                {
    //                    ms.Write(buffer, 0, read);
    //                    totalBytes += read;
    //                }
    //                context.Recycle(buffer);
    //                connection.Send(context, ms.ToArray());
    //                return totalBytes;
    //            }
    //        }

    //        protected override void Send(NetContext context, object message)
    //        {
    //            EnqueueFrame(context, new BinaryFrame((byte[])message));
    //        }
    //    }
    //}
    //class BackAndForeFactory : IProtocolFactory
    //{
    //    public Connection CreateConnection()
    //    {
    //        return null;
    //    }
    //    public IProtocolProcessor GetProcessor()
    //    {
    //        return new BackAndFore();
    //    }
    //    class BackAndFore : ProtocolProcessor
    //    {
    //        protected override int ProcessIncoming(NetContext context, Connection connection, Stream incomingBuffer)
    //        {
    //            if (incomingBuffer.Length < 4) return 0;
    //            int len = (incomingBuffer.ReadByte() << 24) | (incomingBuffer.ReadByte() << 16) |
    //                      (incomingBuffer.ReadByte() << 8) | (incomingBuffer.ReadByte());

    //            int packetLen = 4 + len;
    //            if (incomingBuffer.Length < packetLen) return 0;

    //            var raw = new byte[len];
    //            int read, offset = 0;
    //            while ((read = incomingBuffer.Read(raw, offset, len)) > 0)
    //            {
    //                offset += read;
    //                len -= read;
    //            }
    //            Console.WriteLine("back to client: " + Encoding.UTF8.GetString(raw));
    //            return packetLen;
    //        }
    //        protected override void Send(NetContext context, object message)
    //        {
    //            EnqueueFrame(context, new BigEndianInt32Frame(Encoding.UTF8.GetByteCount((string) message), false));
    //            EnqueueFrame(context, new StringFrame((string)message));
    //        }
    //    }
    //}
    
}
