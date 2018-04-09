using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StackExchange.NetGain
{
    public class BasicHttpResponseProcessor : HttpProcessor
    {
        
        protected override void Send(NetContext context, Connection connection, object message)
        {
            EnqueueFrame(context, new StringFrame((string)message));
        }
        protected override int ProcessIncoming(NetContext context, Connection connection, System.IO.Stream incomingBuffer)
        {
            throw new NotSupportedException(); // not expecting more incoming
        }
        public void SendShutdown(NetContext context)
        {
            EnqueueFrame(context, ShutdownFrame.Default);
        }
    }
}
