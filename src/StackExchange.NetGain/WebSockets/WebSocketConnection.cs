
using System;
using System.Net;

namespace StackExchange.NetGain.WebSockets
{
    public class WebSocketConnection : Connection
    {
        public WebSocketConnection(EndPoint endpoint)
        {
            RequestLine = "/";
            Host = endpoint.ToString();
        }
        public string Host { get; set; }
        public string Origin { get; set; }
        public string RequestLine { get; set; }
        public string Protocol { get; set; }


        internal WebSockets.IExtension[] Extensions { get; private set; }
        internal void SetExtensions(WebSockets.IExtension[] extensions)
        {
            this.Extensions = extensions;
        }

        public bool AllowIncomingDataFrames
        {
            get { return HasFlag(WebSocketFlags.AllowIncomingDataFrames); }
            set { SetFlag(WebSocketFlags.AllowIncomingDataFrames, value); }
        }
        internal bool HasSentClose
        {
            get { return HasFlag(WebSocketFlags.HasSentClose); }
            set { SetFlag(WebSocketFlags.HasSentClose, value);}
        }

        public int MaxCharactersPerFrame { get; set; }

        private volatile WebSocketFlags flags = WebSocketFlags.AllowIncomingDataFrames;
        
        private bool HasFlag(WebSocketFlags flag)
        {
            return (flags & flag) != 0;
        }
        private void SetFlag(WebSocketFlags flag, bool value)
        {
            if (value) flags |= flag;
            else flags &= ~flag;
        }

        [Flags]
        enum WebSocketFlags : byte
        {
            AllowIncomingDataFrames = 1,
            HasSentClose = 2
        }
    }
}
