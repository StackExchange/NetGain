
using System;
using System.Collections.Generic;
namespace StackExchange.NetGain.WebSockets
{
    public interface IExtensionFactory
    {
        bool IsMatch(string name);
        IExtension CreateExtension(string name);
        string GetRequestHeader();
    }
    public interface IExtension : IDisposable
    {
        string GetResponseHeader();
        IEnumerable<WebSocketsFrame> ApplyIncoming(NetContext context, WebSocketConnection connection, WebSocketsFrame frame);
        IEnumerable<WebSocketsFrame> ApplyOutgoing(NetContext context, WebSocketConnection connection, WebSocketsFrame frame);
    }
}
