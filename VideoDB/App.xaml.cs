using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace VideoDB
{
    public partial class App : Application
    {
        private static readonly object LogLock = new object();
        private static string logPath = string.Empty;

        public App()
        {
            logPath = ResolveLogPath();
            LogStartup("Application process started" + Environment.NewLine +
                       "Version: " + typeof(App).Assembly.GetName().Version + Environment.NewLine +
                       "Runtime: " + RuntimeInformation.FrameworkDescription + Environment.NewLine +
                       "OS: " + RuntimeInformation.OSDescription + Environment.NewLine +
                       "Architecture: " + RuntimeInformation.ProcessArchitecture + Environment.NewLine +
                       "Base directory: " + AppContext.BaseDirectory + Environment.NewLine +
                       "Current directory: " + Environment.CurrentDirectory + Environment.NewLine +
                       "Log path: " + logPath);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            Startup += App_Startup;
            Exit += App_Exit;
        }

        public static void LogStartup(string message)
        {
            try
            {
                lock (LogLock)
                {
                    string entry =
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " +
                        message + Environment.NewLine;
                    File.AppendAllText(logPath, entry, new UTF8Encoding(false));
                }
            }
            catch
            {
                // A logging failure must never cause another startup failure.
            }
        }

        private static string ResolveLogPath()
        {
            string preferredPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

            try
            {
                File.AppendAllText(preferredPath, string.Empty);
                return preferredPath;
            }
            catch
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VideoDB",
                    "Logs");
                Directory.CreateDirectory(logDirectory);
                return Path.Combine(logDirectory, "startup.log");
            }
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            LogStartup("WPF startup event; arguments: " + string.Join(" | ", e.Args));
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            LogStartup("Application exited with code " + e.ApplicationExitCode);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatal("Dispatcher unhandled exception", e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogFatal(
                "AppDomain unhandled exception; terminating=" + e.IsTerminating,
                e.ExceptionObject as Exception);
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogFatal("Unobserved task exception", e.Exception);
            e.SetObserved();
        }

        private static void LogFatal(string context, Exception? exception)
        {
            LogStartup("FATAL: " + context + Environment.NewLine +
                       (exception?.ToString() ?? "Unknown exception"));

            try
            {
                MessageBox.Show(
                    "VideoDB could not continue." + Environment.NewLine + Environment.NewLine +
                    "Error details were written to:" + Environment.NewLine + logPath,
                    "VideoDB startup error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }
}
