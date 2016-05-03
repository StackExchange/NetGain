namespace StackExchange.NetGain.Logging
{
    public class NoOpLogger : ILog {
        public void Error(string value) { }

        public void Error(string format, params object[] arg) { }

        public void Info(string value) { }

        public void Info(string format, params object[] arg) { }

        public void Debug(string value) { }

        public void Debug(string format, params object[] arg) { }
    }
}