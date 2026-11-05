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
        ClosingAuction,
        PreMarket,
        AfterHours
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
        private static readonly TimeZoneInfo EasternTz = FindEasternTimeZone();
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

        private static TimeZoneInfo FindEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone(
                        "EST", TimeSpan.FromHours(-5), "US Eastern", "EST");
                }
            }
        }

        /// <summary>Current US Eastern time; DST transitions follow the Windows time-zone rules.</summary>
        public static DateTime NowEastern
        {
            get { return TimeZoneInfo.ConvertTime(Now, ChinaTz, EasternTz); }
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

        private static bool IsUSWeekday(DateTime date)
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

        /// <summary>Next US pre-market start, expressed in China Standard Time.</summary>
        public static DateTime NextOpenUS(DateTime now)
        {
            DateTime et = TimeZoneInfo.ConvertTime(now, ChinaTz, EasternTz);
            if (IsUSWeekday(et.Date) && et.TimeOfDay < new TimeSpan(4, 0, 0))
                return TimeZoneInfo.ConvertTime(et.Date + new TimeSpan(4, 0, 0), EasternTz, ChinaTz);
            for (int i = 1; i <= 10; i++)
            {
                DateTime d = et.Date.AddDays(i);
                if (IsUSWeekday(d))
                    return TimeZoneInfo.ConvertTime(d + new TimeSpan(4, 0, 0), EasternTz, ChinaTz);
            }
            return now.Date.AddDays(1);
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
        /// US session clock in Eastern time: 04:00-09:30 pre-market, 09:30-16:00 regular,
        /// 16:00-20:00 after-hours; pre/after windows display with a tag. US-only holidays
        /// are not enumerated here; the stale-quote fallback hides them automatically.
        /// </summary>
        public static SessionInfo GetUS(DateTime now)
        {
            DateTime et = TimeZoneInfo.ConvertTime(now, ChinaTz, EasternTz);
            bool tradingDay = IsUSWeekday(et.Date);
            TimeSpan t = et.TimeOfDay;
            SessionInfo s = new SessionInfo();
            s.Now = now;
            s.IsTradingDay = tradingDay;

            if (!tradingDay)
            {
                s.Kind = SessionKind.Closed;
                s.Label = "休市";
                s.Detail = NextOpenUSDetail(now);
                s.CanShow = false;
                return s;
            }

            if (t < new TimeSpan(4, 0, 0))
            {
                s.Kind = SessionKind.Closed;
                s.Label = "未开盘";
                s.Detail = BeijingWindow(et.Date + new TimeSpan(4, 0, 0), null) + " 盘前";
                s.CanShow = false;
            }
            else if (t < new TimeSpan(9, 30, 0))
            {
                s.Kind = SessionKind.PreMarket;
                s.Label = "盘前";
                s.Detail = BeijingWindow(et.Date + new TimeSpan(4, 0, 0),
                    et.Date + new TimeSpan(9, 30, 0));
                s.CanShow = true;
            }
            else if (t < new TimeSpan(16, 0, 0))
            {
                s.Kind = SessionKind.Trading;
                s.Label = "交易中";
                s.Detail = BeijingWindow(et.Date + new TimeSpan(9, 30, 0),
                    et.Date + new TimeSpan(16, 0, 0));
                s.CanShow = true;
            }
            else if (t < new TimeSpan(20, 0, 0))
            {
                s.Kind = SessionKind.AfterHours;
                s.Label = "盘后";
                s.Detail = BeijingWindow(et.Date + new TimeSpan(16, 0, 0),
                    et.Date + new TimeSpan(20, 0, 0));
                s.CanShow = true;
            }
            else
            {
                s.Kind = SessionKind.Closed;
                s.Label = "已收盘";
                s.Detail = NextOpenUSDetail(now);
                s.CanShow = false;
            }
            return s;
        }

        /// <summary>An ET window rendered in Beijing clock so tooltips stay consistent.</summary>
        private static string BeijingWindow(DateTime etStart, DateTime? etEnd)
        {
            string start = TimeZoneInfo.ConvertTime(etStart, EasternTz, ChinaTz).ToString("HH:mm");
            if (etEnd == null) return start;
            return start + "-" + TimeZoneInfo.ConvertTime(etEnd.Value, EasternTz, ChinaTz).ToString("HH:mm");
        }

        private static string NextOpenUSDetail(DateTime now)
        {
            DateTime d = NextOpenUS(now);
            int diff = (int)(d.Date - now.Date).TotalDays;
            string prefix;
            if (diff == 0) prefix = "今日 ";
            else if (diff == 1) prefix = "明日 ";
            else if (diff == 2) prefix = "后天 ";
            else prefix = DayName(d.DayOfWeek) + " ";
            return prefix + d.ToString("HH:mm") + " 盘前";
        }

        /// <summary>"盘前"/"盘后" when an Eastern-time quote falls in a pre/after window.</summary>
        public static string USSessionTag(DateTime eastern)
        {
            TimeSpan t = eastern.TimeOfDay;
            if (t >= new TimeSpan(4, 0, 0) && t < new TimeSpan(9, 30, 0)) return "盘前";
            if (t >= new TimeSpan(16, 0, 0) && t < new TimeSpan(20, 0, 0)) return "盘后";
            return "";
        }

        /// <summary>
        /// Combined session for the watched markets: visible while any included market
        /// (A-share, HKEX, or US pre/after) is in a showable window; closed only when
        /// all of them are.
        /// </summary>
        public static SessionInfo GetCombined(DateTime now, bool includeHK, bool includeUS)
        {
            SessionInfo a = Get(now);
            SessionInfo h = includeHK ? GetHK(now) : null;
            SessionInfo u = includeUS ? GetUS(now) : null;
            if (a.CanShow) return a;
            if (h != null && h.CanShow) return h;
            if (u != null && u.CanShow) return u;

            SessionInfo s = new SessionInfo();
            s.Now = now;
            s.Kind = SessionKind.Closed;
            s.IsTradingDay = a.IsTradingDay || (h != null && h.IsTradingDay) ||
                             (u != null && u.IsTradingDay);
            s.Label = "休市";
            s.CanShow = false;

            DateTime next = NextOpenA(now);
            string detail = a.Detail;
            DateTime nextHK = h != null ? NextOpenHK(now) : DateTime.MaxValue;
            DateTime nextUS = u != null ? NextOpenUS(now) : DateTime.MaxValue;
            if (nextHK < next) { next = nextHK; detail = h.Detail; }
            if (nextUS < next) { next = nextUS; detail = u.Detail; }
            s.Detail = detail;
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
