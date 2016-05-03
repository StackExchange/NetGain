using System;

namespace StackExchange.NetGain.Logging
{
    public class ConsoleLogger : ILog
    {
        public void Error(string value)
        {
             Console.Error.WriteLine(value);
        }

        public void Error(string format, params object[] arg)
        {
            Console.Error.WriteLine(format, arg);
        }

        public void Info(string value)
        {
            Console.WriteLine(value);
        }

        public void Info(string format, params object[] arg)
        {
            Console.WriteLine(format, arg);
        }

        public void Debug(string value)
        {
            System.Diagnostics.Debug.WriteLine(value);
        }

        public void Debug(string format, params object[] arg)
        {
            System.Diagnostics.Debug.WriteLine(format, arg);
        }
    }
}