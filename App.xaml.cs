using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AutoKey
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handling - log to file for debugging
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogError("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogError("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogError("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };
        }

        private static void LogError(string source, Exception? ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AutoKey", "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, $"error_{DateTime.Now:yyyyMMdd}.log");
                string msg = $"[{DateTime.Now:HH:mm:ss}] {source}: {ex}\r\n\r\n";
                File.AppendAllText(logFile, msg);
            }
            catch { }
        }
    }
}
