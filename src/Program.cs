using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AStockTicker
{
    internal static class Program
    {
        private const string MutexName = "AStockTicker.SingleInstance.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--settings")
            {
                SettingsForm settings = new SettingsForm(AppConfig.Load());
                settings.TopMost = true;
                Application.Run(settings);
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: stay quiet instead of opening a second copy.
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    LogError(e.Exception);
                    MessageBox.Show("程序遇到错误：" + e.Exception.Message + "\r\n\r\n详细信息已写入 error.log。",
                        "A股行情条", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    LogError(e.ExceptionObject as Exception);
                };

                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                ServicePointManager.DefaultConnectionLimit = 8;

                DebugLog.Write("Program start");
                AppConfig config = AppConfig.Load();
                if (config.AutoStart)
                {
                    // Re-point the Run entry if the folder was moved.
                    StartupManager.Set(true);
                }

                Application.Run(new TickerForm(config));
                GC.KeepAlive(mutex);
            }
        }

        private static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
