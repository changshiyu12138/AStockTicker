using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AStockTicker
{
    /// <summary>Headless harness: compile instead of Program.cs for console self-tests.</summary>
    internal static class TestRunner
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 1 && args[0] == "--dump")
                {
                    Dump();
                    return 0;
                }
                if (args.Length >= 1 && args[0] == "--states")
                {
                    States();
                    return 0;
                }
                if (args.Length >= 2 && args[0] == "--search")
                {
                    Search(args[1]);
                    return 0;
                }
                if (args.Length >= 2 && args[0] == "--intraday")
                {
                    Intraday(args[1]);
                    return 0;
                }
                if (args.Length >= 1 && args[0] == "--selftest")
                {
                    return SelfTest() ? 0 : 1;
                }
                Console.WriteLine("usage: AStockTickerTest --dump | --states | --search <code/name> | --intraday <code> | --selftest");
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                return 3;
            }
        }

        private static void Dump()
        {
            AppConfig config = AppConfig.Load();
            Console.WriteLine("config: " + config.ConfigPath);
            Console.WriteLine("stocks: " + config.Stocks.Count + "  refresh=" + config.RefreshSeconds +
                              "s carousel=" + config.CarouselSeconds + "s");
            DateTime now = MarketClock.Now;
            Console.WriteLine("china now: " + now.ToString("yyyy-MM-dd HH:mm:ss") + " (" + now.DayOfWeek + ")");
            SessionInfo s = MarketClock.Get(now);
            Console.WriteLine("session: " + s.Label + " | " + s.Detail + " | canShow=" + s.CanShow +
                              " | tradingDay=" + s.IsTradingDay);

            QuoteBatch batch = QuoteService.Fetch(config.Stocks);
            Console.WriteLine("fetch: " + batch.Quotes.Count + " quotes, error=" + (batch.Error ?? "none") +
                              ", today=" + batch.HasTodayData + ", latest=" + batch.LatestQuoteTime.ToString("HH:mm:ss"));
            foreach (Quote q in batch.Quotes)
            {
                Console.WriteLine(string.Format("{0,-9} {1,-8} {2,10} {3,8}  prev={4} time={5}",
                    q.Code, q.Name, q.PriceText, q.ChangeText, q.PrevClose.ToString("F2"),
                    q.HasTime ? q.Time.ToString("MM-dd HH:mm:ss") : "-"));
            }
        }

        private static void Search(string query)
        {
            Console.OutputEncoding = Encoding.UTF8;
            List<StockSuggestion> results = QuoteService.Search(query, 8);
            Console.WriteLine("search [" + query + "]: " + results.Count);
            foreach (StockSuggestion s in results)
                Console.WriteLine(s.Code + "  " + s.Name);
        }

        private static void Intraday(string code)
        {
            Console.OutputEncoding = Encoding.UTF8;
            IntradaySeries s = QuoteService.FetchIntraday(code);
            if (s == null)
            {
                Console.WriteLine("intraday [" + code + "]: null");
                return;
            }
            Console.WriteLine("intraday [" + code + "]: " + s.Prices.Count + " minute prices, date=" + s.Date);
            for (int i = Math.Max(0, s.Prices.Count - 8); i < s.Prices.Count; i++)
                Console.Write(s.Prices[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ");
            Console.WriteLine();
        }

        private static void States()
        {
            DateTime day = new DateTime(2026, 10, 27);
            TimeSpan[] times = new TimeSpan[]
            {
                new TimeSpan(9, 10, 0),
                new TimeSpan(9, 20, 0),
                new TimeSpan(9, 27, 0),
                new TimeSpan(10, 0, 0),
                new TimeSpan(11, 45, 0),
                new TimeSpan(14, 58, 0),
                new TimeSpan(15, 10, 0)
            };
            foreach (TimeSpan t in times)
            {
                SessionInfo s = MarketClock.Get(day + t);
                Console.WriteLine(t.ToString(@"hh\:mm") + "  " + s.Label + "  " + s.Detail +
                                  "  canShow=" + s.CanShow);
            }
            SessionInfo weekend = MarketClock.Get(new DateTime(2026, 10, 25, 10, 0, 0));
            Console.WriteLine("weekend  " + weekend.Label + "  " + weekend.Detail + "  canShow=" + weekend.CanShow);
            SessionInfo holiday = MarketClock.Get(new DateTime(2026, 10, 2, 10, 0, 0));
            Console.WriteLine("holiday  " + holiday.Label + "  " + holiday.Detail + "  canShow=" + holiday.CanShow);
        }

        private static bool SelfTest()
        {
            bool ok = true;
            string[] good = new string[] { "600519", "sh600519", "000001", "sz000001", "430047",
                                           "920002", "600519.SH", "SZ399001", "sh000001" };
            string[] bad = new string[] { "abc", "12345", "sh12345", "60051x", "" };
            foreach (string code in good)
            {
                string n = QuoteService.NormalizeCode(code);
                Console.WriteLine("normalize " + code + " -> " + (n ?? "NULL"));
                if (n == null) ok = false;
                if (code == "920002" && n != "bj920002") ok = false;
            }
            foreach (string code in bad)
            {
                string n = QuoteService.NormalizeCode(code);
                Console.WriteLine("reject " + code + " -> " + (n ?? "NULL"));
                if (n != null) ok = false;
            }

            AppConfig c = new AppConfig();
            c.AddDefaultStocks();
            c.FontSize = 11;
            c.RefreshSeconds = 5;
            c.CarouselSeconds = 8;
            c.ConfigPath = Path.Combine(Path.GetTempPath(), "astockticker-selftest.ini");
            c.Save();
            AppConfig loaded = new AppConfig();
            loaded.AddDefaultStocks();
            loaded.Stocks.Clear();
            foreach (string line in File.ReadAllLines(c.ConfigPath, Encoding.UTF8))
            {
                if (line.StartsWith("FontSize=")) loaded.FontSize = int.Parse(line.Substring(9));
                if (line.StartsWith("RefreshSeconds=")) loaded.RefreshSeconds = int.Parse(line.Substring(15));
                if (line.StartsWith("CarouselSeconds=")) loaded.CarouselSeconds = int.Parse(line.Substring(16));
            }
            Console.WriteLine("config roundtrip font=" + loaded.FontSize + " refresh=" + loaded.RefreshSeconds +
                              " carousel=" + loaded.CarouselSeconds);
            if (loaded.FontSize != 11 || loaded.RefreshSeconds != 5 || loaded.CarouselSeconds != 8) ok = false;
            try { File.Delete(c.ConfigPath); } catch { }

            Console.WriteLine(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
            return ok;
        }
    }
}
