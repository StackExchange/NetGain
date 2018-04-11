using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;

namespace StackExchange.NetGain.WebSockets
{
    public abstract class WebSocketsSelectorBase : HttpProcessor
    {

        protected override void InitializeClientHandshake(NetContext context, Connection connection)
        {
            throw new NotSupportedException();
        }
        static internal readonly char[] Comma = { ',' };
        protected static void AddMatchingExtensions(StringDictionary headers, Connection connection, object[] availableExtensions)
        {
            if (availableExtensions == null || availableExtensions.Length == 0) return;
            var conn = connection as WebSocketConnection;
            if (conn == null) return;
            List<IExtension> extensions = null;
            var extHeader = headers["Sec-WebSocket-Extensions"];
            if (!string.IsNullOrEmpty(extHeader))
            {
                var extArr = extHeader.Split(Comma);
                for (int i = 0; i < extArr.Length; i++)
                {
                    string extName = extArr[i].Trim();
                    foreach (var extension in availableExtensions)
                    {
                        var extnFactory = extension as IExtensionFactory;
                        IExtension extn;
                        if (extnFactory != null && extnFactory.IsMatch(extName)
                            && (extn = extnFactory.CreateExtension(extName)) != null)
                        {
                            if (extensions == null) extensions = new List<IExtension>();
                            extensions.Add(extn);
                        }
                    }
                }
            }
            if (extensions != null)
            {
                conn.SetExtensions(extensions.ToArray());
            }
        }
        protected override int ProcessIncoming(NetContext context, Connection connection, Stream incomingBuffer)
        {
            int len = FindHeadersLength(incomingBuffer);
            if (len <= 0) return 0;

            incomingBuffer.Position = 0;
            int extraConsumed;
            if (len < NetContext.BufferSize)
            {
                byte[] buffer = null;
                try
                {
                    buffer = context.GetBuffer();
                    NetContext.Fill(incomingBuffer, buffer, len);
                    using (var ms = new MemoryStream(buffer, 0, len))
                    {
                        extraConsumed = ProcessHeadersAndUpgrade(context, connection, ms, incomingBuffer, len);
                    }
                }
                finally
                {
                    context.Recycle(buffer);
                }
            }
            else
            {
                using (var ss = new SubStream(incomingBuffer, len))
                {
                    extraConsumed = ProcessHeadersAndUpgrade(context, connection, ss, incomingBuffer, len);
                }
            }
            if (extraConsumed < 0) return 0; // means: wasn't usable (yet)

            return len + extraConsumed;
        }


        protected abstract int ProcessHeadersAndUpgrade(NetContext context, Connection connection, Stream input, Stream additionalData, int additionalDataStart);
    }
}
