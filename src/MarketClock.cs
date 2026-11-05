using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AStockTicker
{
    public enum SessionKind
    {
        Closed,
        OpeningAuction,
        PreOpen,
        Trading,
        ClosingAuction
    }

    public class SessionInfo
    {
        public SessionKind Kind;
        public string Label;
        public string Detail;
        public bool CanShow;
        public bool IsTradingDay;
        public DateTime Now;

        public string Key
        {
            get { return Kind + "|" + Label + "|" + Detail; }
        }

        public string FullText
        {
            get { return Label + " · " + Detail; }
        }
    }

    /// <summary>
    /// A-share session clock in China Standard Time.
    /// 09:15-09:25 opening auction, 09:25-09:30 waiting, 09:30-11:30 trading,
    /// 13:00-14:57 trading, 14:57-15:00 closing auction, everything else closed.
    /// </summary>
    public static class MarketClock
    {
        private static readonly TimeZoneInfo ChinaTz = FindChinaTimeZone();
        private static readonly HashSet<string> Holidays = BuildHolidays();

        public static DateTime Now
        {
            get
            {
                // Test hook: lets the headless harness render a specific session.
                string fake = Environment.GetEnvironmentVariable("ASTOCK_FAKE_NOW");
                if (!string.IsNullOrEmpty(fake))
                {
                    DateTime parsed;
                    if (DateTime.TryParseExact(fake, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsed))
                        return parsed;
                }
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz);
            }
        }

        private static TimeZoneInfo FindChinaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone(
                        "CST", TimeSpan.FromHours(8), "China Standard Time", "CST");
                }
            }
        }

        private static HashSet<string> BuildHolidays()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            // 2025 Shanghai/Shenzhen exchange closures.
            AddRange(set, 2025, 1, 1, 1, 1);
            AddRange(set, 2025, 1, 28, 2, 4);
            AddRange(set, 2025, 4, 4, 4, 4);
            AddRange(set, 2025, 5, 1, 5, 5);
            AddRange(set, 2025, 6, 2, 6, 2);
            AddRange(set, 2025, 10, 1, 10, 8);
            // 2026 closures, per the official exchange notice (2025-12-22).
            AddRange(set, 2026, 1, 1, 1, 2);
            AddRange(set, 2026, 2, 16, 2, 23);
            AddRange(set, 2026, 4, 6, 4, 6);
            AddRange(set, 2026, 5, 1, 5, 1);
            AddRange(set, 2026, 5, 4, 5, 5);
            AddRange(set, 2026, 6, 19, 6, 19);
            AddRange(set, 2026, 9, 25, 9, 25);
            AddRange(set, 2026, 10, 1, 10, 7);
            // New Year's Day 2027 (the remaining 2027 calendar is handled by
            // the stale-quote fallback until the official notice is published).
            AddRange(set, 2027, 1, 1, 1, 1);
            LoadExtraHolidays(set);
            return set;
        }

        /// <summary>Optional holidays.txt next to the exe: one yyyy-MM-dd per line, # comments allowed.</summary>
        private static void LoadExtraHolidays(HashSet<string> set)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "holidays.txt");
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    DateTime d;
                    if (DateTime.TryParseExact(line, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out d))
                        set.Add(d.ToString("yyyy-MM-dd"));
                }
            }
            catch { }
        }

        private static void AddRange(HashSet<string> set, int year, int m1, int d1, int m2, int d2)
        {
            DateTime d = new DateTime(year, m1, d1);
            DateTime end = new DateTime(year, m2, d2);
            while (d <= end)
            {
                set.Add(d.ToString("yyyy-MM-dd"));
                d = d.AddDays(1);
            }
        }

        public static bool IsTradingDay(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) return false;
            return !Holidays.Contains(date.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// HKEX weekday calendar. HK-only closures (Easter, Christmas, typhoon days...)
        /// are not enumerated here; the stale-quote fallback hides them automatically.
        /// </summary>
        public static bool IsHKTradingDay(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        public static DateTime NextOpenA(DateTime now)
        {
            if (IsTradingDay(now.Date) && now.TimeOfDay < new TimeSpan(9, 15, 0))
                return now.Date + new TimeSpan(9, 15, 0);
            DateTime d = now.Date.AddDays(1);
            for (int i = 0; i < 60 && !IsTradingDay(d); i++) d = d.AddDays(1);
            return d + new TimeSpan(9, 15, 0);
        }

        public static DateTime NextOpenHK(DateTime now)
        {
            if (IsHKTradingDay(now.Date) && now.TimeOfDay < new TimeSpan(9, 0, 0))
                return now.Date + new TimeSpan(9, 0, 0);
            DateTime d = now.Date.AddDays(1);
            for (int i = 0; i < 14 && !IsHKTradingDay(d); i++) d = d.AddDays(1);
            return d + new TimeSpan(9, 0, 0);
        }

        /// <summary>
        /// HKEX session clock: 09:00-09:30 pre-open auction, 09:30-12:00 trading,
        /// 12:00-13:00 lunch, 13:00-16:00 trading, 16:00-16:10 closing auction.
        /// </summary>
        public static SessionInfo GetHK(DateTime now)
        {
            bool tradingDay = IsHKTradingDay(now.Date);
            TimeSpan t = now.TimeOfDay;
            SessionInfo s = new SessionInfo();
            s.Now = now;
            s.IsTradingDay = tradingDay;

            if (!tradingDay)
            {
                s.Kind = SessionKind.Closed;
                s.Label = "休市";
                s.Detail = NextOpenHKDetail(now);
                s.CanShow = false;
                return s;
            }

            if (t < new TimeSpan(9, 0, 0))
            {
                s.Kind = SessionKind.Closed;
                s.Label = "未开盘";
                s.Detail = "09:00 开市前时段";
                s.CanShow = false;
            }
            else if (t < new TimeSpan(9, 30, 0))
            {
                s.Kind = SessionKind.OpeningAuction;
                s.Label = "开市前时段";
                s.Detail = "09:00-09:30";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(12, 0, 0))
            {
                s.Kind = SessionKind.Trading;
                s.Label = "交易中";
                s.Detail = "09:30-12:00";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(13, 0, 0))
            {
                s.Kind = SessionKind.Closed;
                s.Label = "午间休市";
                s.Detail = "13:00 继续";
                s.CanShow = false;
            }
            else if (t < new TimeSpan(16, 0, 0))
            {
                s.Kind = SessionKind.Trading;
                s.Label = "交易中";
                s.Detail = "13:00-16:00";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(16, 10, 0))
            {
                s.Kind = SessionKind.ClosingAuction;
                s.Label = "收盘竞价";
                s.Detail = "16:00-16:10";
                s.CanShow = true;
            }
            else
            {
                s.Kind = SessionKind.Closed;
                s.Label = "已收盘";
                s.Detail = NextOpenHKDetail(now);
                s.CanShow = false;
            }
            return s;
        }

        private static string NextOpenHKDetail(DateTime now)
        {
            DateTime d = NextOpenHK(now);
            int diff = (int)(d.Date - now.Date).TotalDays;
            string prefix;
            if (diff == 0) prefix = "今日 ";
            else if (diff == 1) prefix = "明日 ";
            else if (diff == 2) prefix = "后天 ";
            else prefix = DayName(d.DayOfWeek) + " ";
            return prefix + "09:00";
        }

        /// <summary>
        /// Combined session for the watched markets: visible while either the A-share
        /// or the HKEX side is in a showable window; closed only when both are.
        /// </summary>
        public static SessionInfo GetCombined(DateTime now, bool includeHK)
        {
            SessionInfo a = Get(now);
            if (!includeHK) return a;

            SessionInfo h = GetHK(now);
            if (a.CanShow) return a;
            if (h.CanShow) return h;

            SessionInfo s = new SessionInfo();
            s.Now = now;
            s.Kind = SessionKind.Closed;
            s.IsTradingDay = a.IsTradingDay || h.IsTradingDay;
            s.Label = "休市";
            s.CanShow = false;
            s.Detail = NextOpenHK(now) < NextOpenA(now) ? h.Detail : a.Detail;
            return s;
        }

        public static SessionInfo Get(DateTime now)
        {
            bool tradingDay = IsTradingDay(now.Date);
            TimeSpan t = now.TimeOfDay;
            SessionInfo s = new SessionInfo();
            s.Now = now;
            s.IsTradingDay = tradingDay;

            if (!tradingDay)
            {
                s.Kind = SessionKind.Closed;
                s.Label = "休市";
                s.Detail = NextOpenDetail(now);
                s.CanShow = false;
                return s;
            }

            if (t < new TimeSpan(9, 15, 0))
            {
                s.Kind = SessionKind.Closed;
                s.Label = "未开盘";
                s.Detail = "09:15 集合竞价";
                s.CanShow = false;
            }
            else if (t < new TimeSpan(9, 25, 0))
            {
                s.Kind = SessionKind.OpeningAuction;
                s.Label = "集合竞价";
                s.Detail = "09:15-09:25";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(9, 30, 0))
            {
                s.Kind = SessionKind.PreOpen;
                s.Label = "待开盘";
                s.Detail = "09:30 开盘";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(11, 30, 0))
            {
                s.Kind = SessionKind.Trading;
                s.Label = "交易中";
                s.Detail = "09:30-11:30";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(13, 0, 0))
            {
                s.Kind = SessionKind.Closed;
                s.Label = "午间休市";
                s.Detail = "13:00 继续";
                s.CanShow = false;
            }
            else if (t < new TimeSpan(14, 57, 0))
            {
                s.Kind = SessionKind.Trading;
                s.Label = "交易中";
                s.Detail = "13:00-15:00";
                s.CanShow = true;
            }
            else if (t < new TimeSpan(15, 0, 0))
            {
                s.Kind = SessionKind.ClosingAuction;
                s.Label = "收盘竞价";
                s.Detail = "14:57-15:00";
                s.CanShow = true;
            }
            else
            {
                s.Kind = SessionKind.Closed;
                s.Label = "已收盘";
                s.Detail = NextOpenDetail(now);
                s.CanShow = false;
            }
            return s;
        }

        private static string NextOpenDetail(DateTime now)
        {
            if (now.TimeOfDay < new TimeSpan(9, 15, 0) && IsTradingDay(now.Date))
                return "09:15 集合竞价";

            DateTime d = now.Date.AddDays(1);
            for (int i = 0; i < 40 && !IsTradingDay(d); i++) d = d.AddDays(1);

            string prefix;
            int diff = (int)(d.Date - now.Date).TotalDays;
            if (diff == 1) prefix = "明日 ";
            else if (diff == 2) prefix = "后天 ";
            else prefix = DayName(d.DayOfWeek) + " ";
            return prefix + "09:15";
        }

        private static string DayName(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }
    }
}
