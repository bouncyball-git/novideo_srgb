using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace novideo_srgb
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Log("===== App.OnStartup =====");
            Logger.Log("Args: " + (e.Args.Length == 0 ? "<none>" : string.Join(" ", e.Args)));
            Logger.Log("BaseDir: " + AppDomain.CurrentDomain.BaseDirectory);
            Logger.Log("OS: " + Environment.OSVersion + " 64bitOS=" + Environment.Is64BitOperatingSystem +
                       " 64bitProc=" + Environment.Is64BitProcess);
            Logger.Log("CLR: " + Environment.Version);

            try
            {
                Logger.Log("NVIDIA driver: version=" + NvAPIWrapper.NVIDIA.DriverVersion +
                           " branch=" + NvAPIWrapper.NVIDIA.DriverBranchVersion +
                           " nvapi-iface=" + NvAPIWrapper.NVIDIA.InterfaceVersionString);
            }
            catch (Exception ex)
            {
                Logger.LogException("Failed to query NVIDIA driver version", ex);
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                base.OnStartup(e);
                Logger.Log("App.OnStartup: base.OnStartup returned");
            }
            catch (Exception ex)
            {
                Logger.LogException("App.OnStartup base.OnStartup threw", ex);
                MessageBox.Show("Startup failed:\n\n" + ex + "\n\nLog: " + Logger.LogPath,
                    "novideo_srgb");
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Log("App.OnExit code=" + e.ApplicationExitCode);
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.LogException("DispatcherUnhandledException", e.Exception);
            MessageBox.Show("Unhandled exception:\n\n" + e.Exception + "\n\nLog: " + Logger.LogPath,
                "novideo_srgb");
            // leave e.Handled = false so we still crash visibly after the message
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
                Logger.LogException("AppDomainUnhandledException IsTerminating=" + e.IsTerminating, ex);
            else
                Logger.Log("AppDomainUnhandledException IsTerminating=" + e.IsTerminating + " obj=" + e.ExceptionObject);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogException("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }
    }
}