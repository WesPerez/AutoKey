using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AutoKey
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private static MemoryMappedFile? _mmf;

        internal static void PublishWindowHandle(IntPtr hWnd)
        {
            try
            {
                _mmf = MemoryMappedFile.CreateOrOpen("AutoKeyMainWindowHandle", 8);
                using var accessor = _mmf.CreateViewAccessor();
                accessor.Write(0, hWnd.ToInt64());
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "AutoKeySingleInstanceMutex", out bool isNewInstance);
            if (!isNewInstance)
            {
                // Read existing window handle from shared memory and send restore message
                try
                {
                    using var existingMmf = MemoryMappedFile.OpenExisting("AutoKeyMainWindowHandle");
                    using var accessor = existingMmf.CreateViewAccessor();
                    long handleValue = accessor.ReadInt64(0);
                    if (handleValue != 0)
                    {
                        IntPtr hWnd = new IntPtr(handleValue);
                        NativeInterop.AllowSetForegroundWindow(-1);
                        NativeInterop.SendMessage(hWnd, NativeInterop.WM_AUTOKEY_RESTORE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                catch { }

                Thread.Sleep(100);
                Shutdown();
                return;
            }

            base.OnStartup(e);

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
