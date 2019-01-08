## Welcome to StackExchange.NetGain! ##

# Warning: reduced maintenance

A few things have changed since this library was written:

- "pipelines" have become the new hotness for high performance low overhead IO code - [see my 3-and-a-bit-part series here](https://blog.marcgravell.com/2018/07/pipe-dreams-part-1.html)
- "Kestrel", combined with pipelines, now provides a great hosting experience for high volume servers
- HTTP/2 (and above) have reduced the usefulness of web-sockets (one of the main drivers for me and NetGain), since the web-sockets drafts for HTTP/2 never really "took"

As of late 2018, Stack Overflow (Stack Exchange) have migrated away from NetGain, instead using Kestrel's **inbuilt** web-socket support over HTTP/1.*; it is entirely possible that as we delve more into HTTP/2, we look more in the direction of [SSE](https://en.wikipedia.org/wiki/Server-sent_events) (`EventSource`).

So: we're not actively working with, or working on, this library. If you have a burning desire to take it further as an official maintainer (perhaps with some tweaks to naming, obviously) - let me know!

---

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
}
```

