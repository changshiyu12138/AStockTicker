using System;
using System.IO;
using System.Text;

namespace AStockTicker
{
    /// <summary>Opt-in diagnostics: set ASTOCK_DEBUG=1 to append events to debug.log next to the exe.</summary>
    internal static class DebugLog
    {
        public static readonly bool Enabled =
            string.Equals(Environment.GetEnvironmentVariable("ASTOCK_DEBUG"), "1", StringComparison.Ordinal);

        public static void Write(string message)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
