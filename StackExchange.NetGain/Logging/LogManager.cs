namespace StackExchange.NetGain.Logging
{
    public abstract class LogManager
    {
        public static LogManager Current { get; set; }

        static LogManager()
        {
            Current = new NoOpLogManager();
        }

        public abstract ILog GetLogger<T>();

        public abstract ILog GetLogger(string logName);
    }
}