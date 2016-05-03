﻿using System;
using System.Collections.Specialized;
using System.Configuration.Install;
using System.Diagnostics;
using System.Net;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using StackExchange.NetGain.Logging;

namespace StackExchange.NetGain
{
    public class TcpService  : ServiceBase
    {
        public TcpService(string configuration, IMessageProcessor processor, IProtocolFactory factory)
        {
            if(processor == null) throw new ArgumentNullException("processor");
            this.processor = processor;
            this.factory = factory;
            Configuration = configuration;
            ServiceName = processor.Name;

            MaxIncomingQuota = TcpHandler.DefaultMaxIncomingQuota;
            MaxOutgoingQuota = TcpHandler.DefaultMaxOutgoingQuota;
        }

        public string Configuration { get; set; }
        public IPEndPoint[] Endpoints { get; set; }
        protected virtual void Configure()
        {
            processor.Configure(this);
        }

        public const string DefaultServiceName = "SocketServerLocal";

        private static readonly ILog log = LogManager.Current.GetLogger<TcpService>();
        private TcpServer server;
        private IMessageProcessor processor;
        private IProtocolFactory factory;
        public void StartService()
        {
            if (processor == null) throw new ObjectDisposedException(GetType().Name);
            if(server == null)
            {
                var tmp = new TcpServer();
                tmp.MessageProcessor = processor;
                tmp.ProtocolFactory = factory;
                tmp.Backlog = 100;
                if (Interlocked.CompareExchange(ref server, tmp, null) == null)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            if(!Environment.UserInteractive)
                            {
                                // why? you may ask; because I need win32 to acknowledge the service is started so we can query the names etc
                                Thread.Sleep(250);
                            }
                                
                            string svcName = GetServiceName();
                            if (!string.IsNullOrEmpty(svcName))
                            {
                                ActualServiceName = svcName;
                            }
                            Configure();
                            tmp.MaxIncomingQuota = MaxIncomingQuota;
                            tmp.MaxOutgoingQuota = MaxOutgoingQuota;
                            tmp.Start(Configuration, Endpoints);
                        } catch (Exception ex)
                        {
                            log.Error(ex.Message);
                            Stop(); // argh!
                        }
                    });
                    
                }
            }
        }

        private string actualServiceName;
        public string ActualServiceName
        {
            get { return actualServiceName ?? ServiceName;  }
            set { actualServiceName = value; }
        }
        static String GetServiceName() // http://stackoverflow.com/questions/1841790/how-can-a-windows-service-determine-its-servicename
        {
            // Calling System.ServiceProcess.ServiceBase::ServiceNamea allways returns
            // an empty string,
            // see https://connect.microsoft.com/VisualStudio/feedback/ViewFeedback.aspx?FeedbackID=387024

            // So we have to do some more work to find out our service name, this only works if
            // the process contains a single service, if there are more than one services hosted
            // in the process you will have to do something else

            int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            String query = "SELECT * FROM Win32_Service where ProcessId = " + processId;
            using (var searcher = new System.Management.ManagementObjectSearcher(query))
            {

                foreach (System.Management.ManagementObject queryObj in searcher.Get())
                {
                    return queryObj["Name"].ToString();
                }
            }
            return null;
        }
        
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(processor != null) processor.Dispose();
                if(server != null) ((IDisposable) server).Dispose();
            }
            processor = null;
            factory = null;
            server = null;
            base.Dispose(disposing);
        }
        protected override void OnStart(string[] args)
        {
            StartService();
        }
        
        public void StopService()
        {
            TcpServer tmp;
            if((tmp = Interlocked.Exchange(ref server, null)) != null)
            {
                tmp.Stop();
            }
        }
        protected override void OnStop()
        {
            StopService();
        }
        private static void LogWhileDying(object ex)
        {
            Exception typed = ex as Exception;
            log.Error(typed == null ? Convert.ToString(ex) : typed.Message);
            if(typed != null)
            {
                log.Error(typed.StackTrace);
            }
#if DEBUG
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
#endif
        }

        public static string InstallerServiceName { get; set; }

        public static int Run<T>(string configuration, string[] args, IPEndPoint[] endpoints, IProtocolFactory protocolFactory)
            where T : IMessageProcessor, new()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) => LogWhileDying(e.ExceptionObject);
                string name = null;
                bool uninstall = false, install = false, benchmark = false, hasErrors = false;
                for (int i = 0; i < args.Length; i++ )
                {
                    switch(args[i])
                    {
                        case "-u": uninstall = true; break;
                        case "-i": install = true; break;
                        case "-b": benchmark = true; break;
                        default:
                            if(args[i].StartsWith("-n:"))
                            {
                                name = args[i].Substring(3);
                            } else
                            {
                                log.Error("Unknown argument: " + args[i]);
                                hasErrors = true;
                            }
                            break;
                    }
                }
                if (hasErrors)
                {
                    log.Error("Support flags:");
                    log.Error("-i\tinstall service");
                    log.Error("-u\tuninstall service");
                    log.Error("-b\tbenchmark");
                    log.Error("-n:name\toverride service name");
                    log.Error("(no args) execute in console");
                    return -1;
                }
                if(uninstall)
                {
                    log.Info("Uninstalling service...");
                    InstallerServiceName = name;
                    ManagedInstallerClass.InstallHelper(new string[] { "/u", typeof(T).Assembly.Location });
                }
                if(install)
                {
                    log.Info("Installing service...");
                    InstallerServiceName = name;
                    ManagedInstallerClass.InstallHelper(new string[] { typeof(T).Assembly.Location });
                        
                }
                if(install || uninstall)
                {
                    log.Info("(done)");
                    return 0;
                }
                if(benchmark)
                {
                    var factory = BasicBinaryProtocolFactory.Default;
                    using (var svc = new TcpService("", new EchoProcessor(), factory))
                    {
                        svc.MaxIncomingQuota = -1;
                        log.Info("Running benchmark using " + svc.ServiceName + "....");
                        svc.StartService();
                        svc.RunEchoBenchmark(1, 500000, factory);
                        svc.RunEchoBenchmark(50, 10000, factory);
                        svc.RunEchoBenchmark(100, 5000, factory);
                        svc.StopService();
                    }
                    return 0;
                }

                if (Environment.UserInteractive)// user facing, explicitly using the Console instead of configured logger
                {
                    using (var messageProcessor = new T())
                    using (var svc = new TcpService(configuration, messageProcessor, protocolFactory))
                    {
                        svc.Endpoints = endpoints;
                        if (!string.IsNullOrEmpty(name)) svc.ActualServiceName = name;
                        svc.StartService();
                        Console.WriteLine("Running " + svc.ActualServiceName +
                                            " in interactive mode; press any key to quit");
                        Console.ReadKey();
                        Console.WriteLine("Exiting...");
                        svc.StopService();
                    }
                    return 0;
                }
                else
                {
                    var svc = new TcpService(configuration, new T(), protocolFactory);
                    svc.Endpoints = endpoints;
                    ServiceBase.Run(svc);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                LogWhileDying(ex);
                return -1;
            }
        }
        internal class EchoProcessor : IMessageProcessor
        {
            public string Name { get { return "Echo"; } }
            public string Description { get { return "Garbage in, garbage out"; } }
            void IMessageProcessor.Configure(TcpService service)
            {
                service.Endpoints = new[] {new IPEndPoint(IPAddress.Loopback, 5999)};
            }
            void IMessageProcessor.StartProcessor(NetContext context, string configuration) { }
            void IMessageProcessor.EndProcessor(NetContext context) { }
            void IMessageProcessor.Heartbeat(NetContext context) { }
            void IDisposable.Dispose() { }
            void IMessageProcessor.OpenConnection(NetContext context, Connection connection) { }
            void IMessageProcessor.CloseConnection(NetContext context, Connection connection) { }
            void IMessageProcessor.Authenticate(NetContext context, Connection connection, StringDictionary claims) { }
            void IMessageProcessor.AfterAuthenticate(NetContext context, Connection connection) { }
            void IMessageProcessor.Received(NetContext context, Connection connection, object message)
            { // right back at you!
                connection.Send(context, message);
            }
            void IMessageProcessor.Flushed(NetContext context, Connection connection) { }

            void IMessageProcessor.OnShutdown(NetContext context, Connection conn) { }
        }
        internal void RunEchoBenchmark(int clients, int iterations, IProtocolFactory factory)
        {
            Thread[] threads = new Thread[clients];
            int remaining = clients;
            ManualResetEvent evt = new ManualResetEvent(false);
            Stopwatch watch = new Stopwatch();
            long opsPerSecond;
            //ThreadStart work = () => RunEchoClient(iterations, ref remaining, evt, watch, factory);

            //for (int i = 0; i < clients; i++)
            //{
            //    threads[i] = new Thread(work);
            //}

            //for (int i = 0; i < clients; i++)
            //{
            //    threads[i].Start();
            //}
            //for (int i = 0; i < clients; i++)
            //{
            //    threads[i].Join();
            //}
            //watch.Stop();
            //opsPerSecond = watch.ElapsedMilliseconds == 0 ? -1 : (clients * iterations * 1000) / watch.ElapsedMilliseconds;
            //log.Info("Total elapsed: {0}ms, {1}ops/s (individual clients)", watch.ElapsedMilliseconds, opsPerSecond);


            var endpoints = Enumerable.Repeat(new IPEndPoint(IPAddress.Loopback, 5999), clients).ToArray();
            var tasks = new Task[iterations];
            byte[] message = new byte[1000];
            new Random(123456).NextBytes(message);
            using(var clientGroup = new TcpClientGroup())
            {
                clientGroup.MaxIncomingQuota = -1;
                clientGroup.ProtocolFactory = factory;
                clientGroup.Open(endpoints);
                watch = Stopwatch.StartNew();
                
                for(int i = 0 ; i < iterations ; i++)
                {
                    tasks[i] = clientGroup.Execute(message);
                }
                Task.WaitAll(tasks);
                watch.Stop();
            }
            opsPerSecond = watch.ElapsedMilliseconds == 0 ? -1 : (iterations * 1000) / watch.ElapsedMilliseconds;
            log.Info("Total elapsed: {0}ms, {1}ops/s (grouped clients)", watch.ElapsedMilliseconds, opsPerSecond);


        }
        void RunEchoClient(int iterations, ref int outstanding, ManualResetEvent evt, Stopwatch mainWatch, IProtocolFactory factory)
        {

            Task<object> last = null;
            var message = Encoding.UTF8.GetBytes("hello");
            using (var client = new TcpClient())
            {
                client.ProtocolFactory = factory;
                client.Open(new IPEndPoint(IPAddress.Loopback, 5999));

                if (Interlocked.Decrement(ref outstanding) == 0)
                {
                    mainWatch.Start();
                    evt.Set();
                }
                else evt.WaitOne();

                var watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                    last = client.Execute(message);
                last.Wait();
                watch.Stop();
                //log.Info("{0}ms", watch.ElapsedMilliseconds);
            }
        }

        public int MaxIncomingQuota { get; set; }
        public int MaxOutgoingQuota { get; set; }
    }
}
