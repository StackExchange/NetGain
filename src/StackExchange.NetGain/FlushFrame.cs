
namespace StackExchange.NetGain
{
    public sealed class FlushFrame : IFrame
    {
        void IFrame.Write(NetContext context, Connection connection, System.IO.Stream stream)
        {
            // nothing to do
        }

        bool IFrame.Flush
        {
            get { return true; }
        }
    }
}
