using System.Configuration;
using System.Data;
using System.Windows;

namespace QrCodeParser
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ProcessManager.GetProcessLock(out var holdsLock);
            if (!holdsLock)
            {
                Application.Current.Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            ProcessManager.ReleaseLock();
        }

    }

}
