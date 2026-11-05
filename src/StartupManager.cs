using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AStockTicker
{
    /// <summary>HKCU Run-key auto start; no administrator rights required.</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AStockTicker";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return false;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
