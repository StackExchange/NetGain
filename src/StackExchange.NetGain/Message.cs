
namespace StackExchange.NetGain
{
    public class Message
    {
        private readonly object value;
        private readonly NetContext context;
        private readonly Connection connection;
        private bool isHandled;
        public bool IsHandled { get { return isHandled; } set { isHandled = value; } }
        public object Value { get { return value; } }
        public NetContext Context { get { return context; } }
        public Connection Connection { get { return connection; } }

        public Message(NetContext context, Connection connection, object value)
        {
            this.context = context;
            this.connection = connection;
            this.value = value;
            isHandled = false;
        }
    }
}
