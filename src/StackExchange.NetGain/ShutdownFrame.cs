
namespace StackExchange.NetGain
{
    public sealed class ShutdownFrame : IFrame
    {
        private static readonly ShutdownFrame @default = new ShutdownFrame();
        public static ShutdownFrame Default { get { return @default; } }
        void IFrame.Write(NetContext context, Connection connection, System.IO.Stream stream)
        {
            connection.Shutdown(context);
        }
        public override string ToString()
        {
            return "[eof]";
        }
        bool IFrame.Flush
        {
            get { return true; }
        }
    }
}
