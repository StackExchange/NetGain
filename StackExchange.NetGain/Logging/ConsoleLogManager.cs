using System;

namespace StackExchange.NetGain.Logging
{
    public class ConsoleLogManager : LogManager
    {
        private static readonly Lazy<ConsoleLogger> Log = new Lazy<ConsoleLogger>(() => new ConsoleLogger()); 
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