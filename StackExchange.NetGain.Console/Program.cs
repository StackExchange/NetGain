
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

                Console.WriteLine("Press any key to end");
                Console.ReadKey();
            }
        }
    } 
    
}
