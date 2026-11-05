using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AStockTicker
{
    /// <summary>A watch-list entry: normalized code (sh600519) plus display name.</summary>
    public class StockItem
    {
        public string Code = "";
        public string Name = "";

        public StockItem() { }

        public StockItem(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public override string ToString()
        {
            return Code + "    " + Name;
        }

        public StockItem Clone()
        {
            return new StockItem(Code, Name);
        }
    }

    /// <summary>Portable ini configuration, stored next to the exe when possible.</summary>
    public class AppConfig
    {
        public List<StockItem> Stocks = new List<StockItem>();
        public int RefreshSeconds = 3;
        public int CarouselSeconds = 5;
        public int FontSize = 10;
        public int OpacityPercent = 94;
        public bool AlwaysOnTop = true;
        public bool HideWhenClosed = true;
        public bool MonoColor = false;
        public bool PrivacyMode = false;
        public int FrostLevel = 2;
        public bool AutoStart = false;
        public int X = -1;
        public int Y = -1;
        public string ConfigPath = "";

        public const int MaxStocks = 12;

        /// <summary>True when the watch list contains any HK symbol (hk00700, hkHSI...).</summary>
        public bool HasHongKong()
        {
            foreach (StockItem s in Stocks)
            {
                if (s != null && s.Code != null &&
                    s.Code.StartsWith("hk", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            c.AddDefaultStocks();
            c.ConfigPath = DefaultPath();

            // If the exe folder is read-only, a previous run may have stored the
            // config under %APPDATA%; prefer that copy when the portable one is absent.
            if (!File.Exists(c.ConfigPath))
            {
                string roaming = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AStockTicker", "config.ini");
                if (File.Exists(roaming)) c.ConfigPath = roaming;
            }

            if (File.Exists(c.ConfigPath))
            {
                try
                {
                    c.Parse(File.ReadAllLines(c.ConfigPath, Encoding.UTF8));
                }
                catch
                {
                    // Corrupt or unreadable config: keep defaults.
                }
            }
            else
            {
                try { c.Save(); }
                catch { }
            }
            c.Normalize();
            return c;
        }

        public static string DefaultPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        }

        public void AddDefaultStocks()
        {
            Stocks.Add(new StockItem("sh000001", "上证指数"));
            Stocks.Add(new StockItem("sz399001", "深证成指"));
            Stocks.Add(new StockItem("sh600519", "贵州茅台"));
        }

        public AppConfig Clone()
        {
            AppConfig c = new AppConfig();
            c.Stocks = new List<StockItem>();
            foreach (StockItem s in Stocks) c.Stocks.Add(s.Clone());
            c.RefreshSeconds = RefreshSeconds;
            c.CarouselSeconds = CarouselSeconds;
            c.FontSize = FontSize;
            c.OpacityPercent = OpacityPercent;
            c.AlwaysOnTop = AlwaysOnTop;
            c.HideWhenClosed = HideWhenClosed;
            c.MonoColor = MonoColor;
            c.PrivacyMode = PrivacyMode;
            c.FrostLevel = FrostLevel;
            c.AutoStart = AutoStart;
            c.X = X;
            c.Y = Y;
            c.ConfigPath = ConfigPath;
            return c;
        }

        public void Normalize()
        {
            if (RefreshSeconds < 1) RefreshSeconds = 1;
            if (RefreshSeconds > 30) RefreshSeconds = 30;
            if (CarouselSeconds < 2) CarouselSeconds = 2;
            if (CarouselSeconds > 30) CarouselSeconds = 30;
            if (FontSize < 8) FontSize = 8;
            if (FontSize > 14) FontSize = 14;
            if (OpacityPercent < 60) OpacityPercent = 60;
            if (OpacityPercent > 100) OpacityPercent = 100;
            if (FrostLevel < 1) FrostLevel = 1;
            if (FrostLevel > 3) FrostLevel = 3;

            List<StockItem> clean = new List<StockItem>();
            List<string> seen = new List<string>();
            foreach (StockItem s in Stocks)
            {
                if (s == null) continue;
                string code = QuoteService.NormalizeCode(s.Code);
                if (code == null) continue;
                if (seen.Contains(code)) continue;
                string name = (s.Name ?? "").Replace(",", " ").Replace("|", " ").Replace("~", " ").Trim();
                if (name.Length == 0) name = code;
                seen.Add(code);
                clean.Add(new StockItem(code, name));
                if (clean.Count >= MaxStocks) break;
            }
            Stocks = clean;
        }

        private void Parse(string[] lines)
        {
            string section = "";
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (section != "general") continue;

                switch (key)
                {
                    case "stocks":
                    case "watchlist":
                        Stocks.Clear();
                        string[] parts = val.Split(',');
                        foreach (string part in parts)
                        {
                            string token = part.Trim();
                            if (token.Length == 0) continue;
                            string[] kv = token.Split('|');
                            string code = QuoteService.NormalizeCode(kv[0]);
                            if (code == null) continue;
                            string name = kv.Length > 1 ? kv[1].Trim() : "";
                            Stocks.Add(new StockItem(code, name));
                        }
                        break;
                    case "refreshseconds":
                        RefreshSeconds = ParseInt(val, RefreshSeconds);
                        break;
                    case "carouselseconds":
                        CarouselSeconds = ParseInt(val, CarouselSeconds);
                        break;
                    case "fontsize":
                        FontSize = ParseInt(val, FontSize);
                        break;
                    case "opacity":
                        OpacityPercent = ParseInt(val, OpacityPercent);
                        break;
                    case "alwaysontop":
                        AlwaysOnTop = ParseBool(val, AlwaysOnTop);
                        break;
                    case "hidewhenclosed":
                        HideWhenClosed = ParseBool(val, HideWhenClosed);
                        break;
                    case "monocolor":
                        MonoColor = ParseBool(val, MonoColor);
                        break;
                    case "privacy":
                        PrivacyMode = ParseBool(val, PrivacyMode);
                        break;
                    case "frostlevel":
                        FrostLevel = ParseInt(val, FrostLevel);
                        break;
                    case "autostart":
                        AutoStart = ParseBool(val, AutoStart);
                        break;
                    case "x":
                        X = ParseInt(val, X);
                        break;
                    case "y":
                        Y = ParseInt(val, Y);
                        break;
                }
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            if (s == null) return fallback;
            string t = s.Trim().ToLowerInvariant();
            if (t == "1" || t == "true" || t == "yes" || t == "on") return true;
            if (t == "0" || t == "false" || t == "no" || t == "off") return false;
            return fallback;
        }

        public string ToIni()
        {
            Normalize();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; A股行情条 配置文件 (UTF-8)");
            sb.AppendLine("; 修改后请重启程序；也可以在托盘菜单 -> 自选股设置 中修改。");
            sb.AppendLine("[General]");
            sb.Append("Stocks=");
            for (int i = 0; i < Stocks.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Stocks[i].Code);
                sb.Append("|");
                sb.Append(Stocks[i].Name);
            }
            sb.AppendLine();
            sb.AppendLine("RefreshSeconds=" + RefreshSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CarouselSeconds=" + CarouselSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("FontSize=" + FontSize.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Opacity=" + OpacityPercent.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("AlwaysOnTop=" + (AlwaysOnTop ? "1" : "0"));
            sb.AppendLine("HideWhenClosed=" + (HideWhenClosed ? "1" : "0"));
            sb.AppendLine("MonoColor=" + (MonoColor ? "1" : "0"));
            sb.AppendLine("Privacy=" + (PrivacyMode ? "1" : "0"));
            sb.AppendLine("FrostLevel=" + FrostLevel.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("AutoStart=" + (AutoStart ? "1" : "0"));
            sb.AppendLine("X=" + X.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Y=" + Y.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Writes the ini; falls back to %APPDATA% when the exe folder is read-only.</summary>
        public void Save()
        {
            Normalize();
            string text = ToIni();
            try
            {
                File.WriteAllText(ConfigPath, text, new UTF8Encoding(false));
            }
            catch
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AStockTicker");
                Directory.CreateDirectory(dir);
                ConfigPath = Path.Combine(dir, "config.ini");
                File.WriteAllText(ConfigPath, text, new UTF8Encoding(false));
            }
        }
    }
}
