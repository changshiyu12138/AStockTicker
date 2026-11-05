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
                if (args.Length >= 2 && args[0] == "--quote")
                {
                    Quote(args[1]);
                    return 0;
                }
                if (args.Length >= 1 && args[0] == "--selftest")
                {
                    return SelfTest() ? 0 : 1;
                }
                Console.WriteLine("usage: AStockTickerTest --dump | --states | --search <code/name> | --intraday <code> | --quote <code[,code...]> | --selftest");
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
            SessionInfo hks = MarketClock.GetHK(now);
            Console.WriteLine("hk session: " + hks.Label + " | " + hks.Detail + " | canShow=" + hks.CanShow +
                              " | tradingDay=" + hks.IsTradingDay);
            SessionInfo us = MarketClock.GetUS(now);
            Console.WriteLine("us session: " + us.Label + " | " + us.Detail + " | canShow=" + us.CanShow +
                              " | et=" + MarketClock.NowEastern.ToString("yyyy-MM-dd HH:mm:ss"));
            bool hasHK = config.HasHongKong();
            bool hasUS = config.HasUS();
            SessionInfo combined = MarketClock.GetCombined(now, hasHK, hasUS);
            Console.WriteLine("combined (hasHK=" + hasHK + " hasUS=" + hasUS + "): " + combined.Label + " | " +
                              combined.Detail + " | canShow=" + combined.CanShow);

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

        private static void Quote(string codes)
        {
            Console.OutputEncoding = Encoding.UTF8;
            List<StockItem> items = new List<StockItem>();
            foreach (string c in codes.Split(','))
            {
                string t = c.Trim();
                if (t.Length > 0) items.Add(new StockItem(t, ""));
            }
            QuoteBatch batch = QuoteService.Fetch(items);
            Console.WriteLine("fetch: " + batch.Quotes.Count + " quotes, error=" + (batch.Error ?? "none") +
                              ", today=" + batch.HasTodayData + ", latest=" + batch.LatestQuoteTime.ToString("HH:mm:ss"));
            foreach (Quote q in batch.Quotes)
            {
                Console.WriteLine(string.Format("{0,-9} {1,-10} {2,12} {3,9}  prev={4,-10} time={5}",
                    q.Code, q.Name, q.PriceText, q.ChangeText, q.PrevClose.ToString("F2"),
                    q.HasTime ? q.Time.ToString("MM-dd HH:mm:ss") : "-"));
            }
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

            Console.WriteLine("-- HKEX sessions --");
            foreach (TimeSpan t in times)
            {
                SessionInfo s = MarketClock.GetHK(day + t);
                Console.WriteLine(t.ToString(@"hh\:mm") + "  " + s.Label + "  " + s.Detail +
                                  "  canShow=" + s.CanShow);
            }
            SessionInfo hkWeekend = MarketClock.GetHK(new DateTime(2026, 10, 25, 10, 0, 0));
            Console.WriteLine("hk weekend  " + hkWeekend.Label + "  " + hkWeekend.Detail + "  canShow=" + hkWeekend.CanShow);

            Console.WriteLine("-- US sessions (Beijing clock, EST = ET+13h) --");
            DateTime usDay = new DateTime(2026, 11, 6);
            foreach (TimeSpan t in new TimeSpan[]
            {
                new TimeSpan(16, 0, 0),
                new TimeSpan(17, 30, 0),
                new TimeSpan(23, 0, 0),
                new TimeSpan(4, 30, 0)
            })
            {
                SessionInfo s = MarketClock.GetUS(usDay + t);
                Console.WriteLine(t.ToString(@"hh\:mm") + "  " + s.Label + "  " + s.Detail +
                                  "  canShow=" + s.CanShow);
            }
            SessionInfo usSatMorning = MarketClock.GetUS(new DateTime(2026, 11, 7, 6, 0, 0));
            Console.WriteLine("sat 06:00  " + usSatMorning.Label + "  " + usSatMorning.Detail +
                              "  canShow=" + usSatMorning.CanShow);
            SessionInfo usSatLate = MarketClock.GetUS(new DateTime(2026, 11, 7, 10, 0, 0));
            Console.WriteLine("sat 10:00  " + usSatLate.Label + "  " + usSatLate.Detail +
                              "  canShow=" + usSatLate.CanShow);

            SessionInfo mixedLunch = MarketClock.GetCombined(day + new TimeSpan(11, 45, 0), true, false);
            SessionInfo mixedBothLunch = MarketClock.GetCombined(day + new TimeSpan(12, 30, 0), true, false);
            Console.WriteLine("mixed 11:45  " + mixedLunch.Label + "  canShow=" + mixedLunch.CanShow);
            Console.WriteLine("mixed 12:30  " + mixedBothLunch.Label + "  canShow=" + mixedBothLunch.CanShow);
            SessionInfo mixedUSNight = MarketClock.GetCombined(day + new TimeSpan(23, 30, 0), false, true);
            Console.WriteLine("mixed 23:30 (US only)  " + mixedUSNight.Label + "  canShow=" + mixedUSNight.CanShow);
        }

        private static bool SelfTest()
        {
            bool ok = true;
            string[][] good = new string[][]
            {
                new string[] { "600519", "sh600519" },
                new string[] { "sh600519", "sh600519" },
                new string[] { "000001", "sz000001" },
                new string[] { "sz000001", "sz000001" },
                new string[] { "430047", "bj430047" },
                new string[] { "920002", "bj920002" },
                new string[] { "600519.SH", "sh600519" },
                new string[] { "SZ399001", "sz399001" },
                new string[] { "sh000001", "sh000001" },
                new string[] { "00700", "hk00700" },
                new string[] { "hk00700", "hk00700" },
                new string[] { "700", "hk00700" },
                new string[] { "hk700", "hk00700" },
                new string[] { "00700.HK", "hk00700" },
                new string[] { "12345", "hk12345" },
                new string[] { "5", "hk00005" },
                new string[] { "2828", "hk02828" },
                new string[] { "02828", "hk02828" },
                new string[] { "hsi", "hkHSI" },
                new string[] { "hkHSI", "hkHSI" },
                new string[] { "HKHSTECH", "hkHSTECH" },
                new string[] { "hscei", "hkHSCEI" },
                new string[] { "510300", "sh510300" },
                new string[] { "159915", "sz159915" },
                new string[] { "588000", "sh588000" },
                new string[] { "161725", "sz161725" },
                new string[] { "usAAPL", "usAAPL" },
                new string[] { "usAAPL.OQ", "usAAPL" },
                new string[] { "aapl.us", "usAAPL" },
                new string[] { "usTSLA", "usTSLA" },
                new string[] { "usDJI", "usDJI" }
            };
            string[] bad = new string[] { "abc", "sh12345", "60051x", "", "hk123456", "hkabc", "hk", "hksi",
                "user", "usd", "us00700" };
            foreach (string[] pair in good)
            {
                string n = QuoteService.NormalizeCode(pair[0]);
                Console.WriteLine("normalize " + pair[0] + " -> " + (n ?? "NULL"));
                if (n != pair[1]) ok = false;
            }
            foreach (string code in bad)
            {
                string n = QuoteService.NormalizeCode(code);
                Console.WriteLine("reject " + code + " -> " + (n ?? "NULL"));
                if (n != null) ok = false;
            }

            // HKEX session clock (2026-11-06 is a Friday).
            DateTime hkDay = new DateTime(2026, 11, 6);
            if (MarketClock.GetHK(hkDay + new TimeSpan(9, 5, 0)).Kind != SessionKind.OpeningAuction) ok = false;
            if (!MarketClock.GetHK(hkDay + new TimeSpan(9, 5, 0)).CanShow) ok = false;
            if (MarketClock.GetHK(hkDay + new TimeSpan(10, 30, 0)).Kind != SessionKind.Trading) ok = false;
            if (MarketClock.GetHK(hkDay + new TimeSpan(12, 30, 0)).CanShow) ok = false;
            if (MarketClock.GetHK(hkDay + new TimeSpan(15, 30, 0)).Kind != SessionKind.Trading) ok = false;
            if (MarketClock.GetHK(hkDay + new TimeSpan(16, 5, 0)).Kind != SessionKind.ClosingAuction) ok = false;
            if (MarketClock.GetHK(hkDay + new TimeSpan(16, 30, 0)).CanShow) ok = false;
            if (MarketClock.GetHK(new DateTime(2026, 11, 7, 10, 0, 0)).CanShow) ok = false;

            // Combined multi-market visibility: any included market keeps the strip alive.
            if (!MarketClock.GetCombined(hkDay + new TimeSpan(11, 45, 0), true, false).CanShow) ok = false;
            if (MarketClock.GetCombined(hkDay + new TimeSpan(12, 30, 0), true, false).CanShow) ok = false;
            if (!MarketClock.GetCombined(hkDay + new TimeSpan(15, 30, 0), true, false).CanShow) ok = false;
            if (MarketClock.GetCombined(hkDay + new TimeSpan(16, 30, 0), true, false).CanShow) ok = false;
            if (MarketClock.GetCombined(hkDay + new TimeSpan(11, 45, 0), false, false).CanShow) ok = false;

            // US sessions (2026-11-06 Friday, EST since Nov 1: Beijing = ET + 13h).
            DateTime usDay = new DateTime(2026, 11, 6);
            if (MarketClock.GetUS(usDay + new TimeSpan(16, 30, 0)).Kind != SessionKind.Closed) ok = false;
            if (MarketClock.GetUS(usDay + new TimeSpan(17, 30, 0)).Kind != SessionKind.PreMarket) ok = false;
            if (!MarketClock.GetUS(usDay + new TimeSpan(17, 30, 0)).CanShow) ok = false;
            if (MarketClock.GetUS(usDay + new TimeSpan(23, 0, 0)).Kind != SessionKind.Trading) ok = false;
            if (MarketClock.GetUS(new DateTime(2026, 11, 7, 6, 0, 0)).Kind != SessionKind.AfterHours) ok = false;
            if (MarketClock.GetUS(new DateTime(2026, 11, 7, 10, 0, 0)).CanShow) ok = false;
            if (MarketClock.GetUS(new DateTime(2026, 11, 8, 6, 0, 0)).Kind != SessionKind.Closed) ok = false;
            if (MarketClock.USSessionTag(new DateTime(2026, 11, 4, 8, 0, 0)) != "盘前") ok = false;
            if (MarketClock.USSessionTag(new DateTime(2026, 11, 4, 17, 0, 0)) != "盘后") ok = false;
            if (MarketClock.USSessionTag(new DateTime(2026, 11, 4, 12, 30, 0)) != "") ok = false;
            if (!MarketClock.GetCombined(usDay + new TimeSpan(23, 30, 0), false, true).CanShow) ok = false;

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
