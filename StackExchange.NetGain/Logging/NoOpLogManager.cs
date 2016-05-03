using System;

namespace StackExchange.NetGain.Logging
{
    public class NoOpLogManager : LogManager
    {
        private static readonly Lazy<ILog> Log = new Lazy<ILog>(() => new NoOpLogger()); 

        public override ILog GetLogger<T>()
        {
            return Log.Value;
        }

        public override ILog GetLogger(string logName)
        {
            return Log.Value;
        }
    }
}