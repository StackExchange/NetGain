namespace StackExchange.NetGain.Logging
{
    public interface ILog
    {
        void Error(string value);

        void Error(string format, params object[] arg);

        void Info(string value);

        void Info(string format, params object[] arg);

        void Debug(string value);

        void Debug(string format, params object[] arg);
    }
}
