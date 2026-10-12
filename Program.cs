using System;
using System.IO;
using System.Windows.Forms;
using FirestormTwwMonitor.UI;

namespace FirestormTwwMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                File.AppendAllText("crash.log", $"[{DateTime.Now}] ThreadException: {e.Exception}\n");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.AppendAllText("crash.log", $"[{DateTime.Now}] UnhandledException: {e.ExceptionObject}\n");
            };

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                File.AppendAllText("crash.log", $"[{DateTime.Now}] Main Catch: {ex}\n");
            }
        }
    }
}
