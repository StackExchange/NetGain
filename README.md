## Welcome to StackExchange.NetGain! ##

**NetGain** supports:

- **[RFC 6455](https://tools.ietf.org/html/rfc6455)**
- all draft versions of the RFC (between hixie-76 and v13, aka RFC 6455)  
-  [hixie-76/hybi-00](https://tools.ietf.org/html/draft-hixie-thewebsocketprotocol-76)
- fixes for various browsers with slightly broken implementations

## Build ##

StackExchange.NetGain is built as a single assembly, **StackExchange.NetGain.dll**.

### NuGet Gallery ###

StackExchange.NetGain is available on the **[NuGet Gallery]**

- **[NuGet Gallery: stackexchange.netgain]**

You can add StackExchange.NetGain to your project with the **NuGet Package Manager**, by using the following command in the **Package Manager Console**.

    PM> Install-Package StackExchange.NetGain

### WebSocket Server Example ###

```csharp
using System;
using System.Net;
using StackExchange.NetGain;
using StackExchange.NetGain.WebSockets;

namespace Example
{
	public class Program
	{
		public static void Main (string[] args)
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
					conn.Send(msg.Context, reply);
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
```

### WebSocket Client Example ###

```csharp
using System;
using System.Net;
using StackExchange.NetGain;
using StackExchange.NetGain.WebSockets;

namespace Example
{
	public class Program
	{
		public static void Main (string[] args)
		{
			using (var client = new TcpClient())
			{
				client.ProtocolFactory = WebSocketClientFactory.Default;
				client.Open(new IPEndPoint(IPAddress.Loopback, 6002));

				var message = Console.ReadLine();

				string resp = (string)client.ExecuteSync(message);
			}
		}
	}
}
```
