
using System.Configuration.Install;
using System.ServiceProcess;

namespace StackExchange.NetGain
{
    public class MessageProcessorServiceInstaller<T> : Installer where T : IMessageProcessor, new()
    {
        public MessageProcessorServiceInstaller()
        {
            // Instantiate installers for process and services.
            var processInstaller = new ServiceProcessInstaller();
            var serviceInstaller = new ServiceInstaller();

            processInstaller.Account = ServiceAccount.NetworkService;
            serviceInstaller.StartType = ServiceStartMode.Automatic;
            using (var proc = new T())
            {
                serviceInstaller.ServiceName = proc.Name;
                serviceInstaller.Description = proc.Description;
            }
            if (!string.IsNullOrEmpty(TcpService.InstallerServiceName))
                serviceInstaller.ServiceName = TcpService.InstallerServiceName; // -n:Foo specified on command line

            // Add installers to collection. Order is not important.
            Installers.Add(serviceInstaller);
            Installers.Add(processInstaller);
        }
    }
}
