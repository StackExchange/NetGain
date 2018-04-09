
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
                Console.WriteLine("Server running");

                Console.ReadKey();
            }
            Console.WriteLine("Server dead; press any key");
            Console.ReadKey();
        }

    } 
    
}
