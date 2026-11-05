using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace AStockTicker
{
    public class Quote
    {
        public string Code = "";
        public string Name = "";
        public double Price;
        public double PrevClose;
        public double Open;
        public double High;
        public double Low;
        public double Change;
        public double ChangePercent;
        public double Volume;   // hands (手)
        public double Amount;   // 10k CNY
        public DateTime Time;
        public bool HasTime;

        /// <summary>"盘前"/"盘后" when this US quote was stamped outside regular hours.</summary>
        public string SessionTag = "";

        public bool Suspended
        {
            get { return Price <= 0.0001; }
        }

        public string PriceText
        {
            get { return Suspended ? "--" : Price.ToString("F2", CultureInfo.InvariantCulture); }
        }

        public string ChangeText
        {
            get
            {
                if (Suspended) return "--";
                string sign = ChangePercent > 0.0001 ? "+" : "";
                return sign + ChangePercent.ToString("F2", CultureInfo.InvariantCulture) + "%";
            }
        }
    }

    /// <summary>A search candidate: normalized code plus display name.</summary>
    public class StockSuggestion
    {
        public string Code = "";
        public string Name = "";

        public StockSuggestion(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public override string ToString()
        {
            return Name + "    " + Code;
        }
    }

    /// <summary>Today's intraday minute prices (oldest first) for the mini chart.</summary>
    public class IntradaySeries
    {
        public List<double> Prices = new List<double>();
        public string Date = "";
    }

    public class QuoteBatch
    {
        public List<Quote> Quotes = new List<Quote>();
        public DateTime FetchedAt;
        public DateTime LatestQuoteTime;
        public bool HasTodayData;
        public string Error;

        public Quote Find(string code)
        {
            foreach (Quote q in Quotes)
                if (string.Equals(q.Code, code, StringComparison.OrdinalIgnoreCase)) return q;
            return null;
        }
    }

    /// <summary>
    /// Tencent quote endpoint (qt.gtimg.cn). One request can carry up to a dozen codes.
    /// HTTPS is preferred, with an automatic plain-HTTP fallback for machines whose
    /// TLS stack cannot negotiate with the endpoint.
    /// </summary>
    public static class QuoteService
    {
        private static readonly object HttpsLock = new object();
        private static bool _httpsWorks = true;
        private static DateTime _httpsRetryAfter = DateTime.MinValue;

        static QuoteService()
        {
            // Endpoints require TLS 1.2+; .NET Framework defaults to TLS 1.0 on older OS builds.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        public static string NormalizeCode(string raw)
        {
            if (raw == null) return null;
            string s = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("\u3000", "");
            if (s.Length == 0) return null;

            if (s.EndsWith(".sh")) s = "sh" + s.Substring(0, s.Length - 3);
            else if (s.EndsWith(".sz")) s = "sz" + s.Substring(0, s.Length - 3);
            else if (s.EndsWith(".bj")) s = "bj" + s.Substring(0, s.Length - 3);
            else if (s.EndsWith(".hk")) s = "hk" + s.Substring(0, s.Length - 3);

            // HK indices: the endpoint is case-sensitive (hkHSI works, hkhsi does not),
            // so aliases in any case map to the canonical mixed-case symbols.
            if (s == "hsi" || s == "hkhsi") return "hkHSI";
            if (s == "hstech" || s == "hkhstech") return "hkHSTECH";
            if (s == "hscei" || s == "hkhscei") return "hkHSCEI";

            if (s.Length == 8)
            {
                string prefix = s.Substring(0, 2);
                string digits = s.Substring(2);
                if ((prefix == "sh" || prefix == "sz" || prefix == "bj") && AllDigits(digits))
                    return prefix + digits;
                return null;
            }

            // HK stocks: 1-5 digit codes, zero-padded to five (hk00700).
            if (s.StartsWith("hk"))
            {
                string rest = s.Substring(2);
                if (rest.Length >= 1 && rest.Length <= 5 && AllDigits(rest))
                    return "hk" + rest.PadLeft(5, '0');
                return null;
            }

            // US symbols, matched on the case-preserved form because symbol case is
            // meaningful ("user" stays a search term, "usAAPL" is a ticker):
            // "usAAPL", "usAAPL.OQ" and explicit "aapl.us" all map to canonical
            // "usAAPL" (the quote endpoint takes the bare symbol). Bare letters
            // ("aapl") stay unnormalized so the name/pinyin search path still
            // triggers; pure digits ("us00700") are not US symbols.
            string orig = raw.Trim().Replace(" ", "").Replace("\u3000", "");
            string usRest = null;
            bool usExplicit = orig.EndsWith(".us", StringComparison.OrdinalIgnoreCase);
            if (usExplicit && orig.Length > 3)
            {
                usRest = orig.Substring(0, orig.Length - 3);
            }
            else if (!usExplicit && orig.StartsWith("us", StringComparison.OrdinalIgnoreCase) &&
                     orig.Length >= 4)
            {
                usRest = orig.Substring(2);
            }
            if (usRest != null)
            {
                bool hasLetter = false, hasUpper = false, hasDigit = false, hasMark = false, ok = true;
                for (int i = 0; i < usRest.Length; i++)
                {
                    char c = usRest[i];
                    if (c >= 'A' && c <= 'Z') { hasLetter = true; hasUpper = true; }
                    else if (c >= 'a' && c <= 'z') { hasLetter = true; }
                    else if (c >= '0' && c <= '9') hasDigit = true;
                    else if (c == '.' || c == '_') hasMark = true;
                    else { ok = false; break; }
                }
                if (ok && hasLetter && (usExplicit || hasUpper || hasDigit || hasMark))
                {
                    int cut = usRest.IndexOfAny(CodeSeparators);
                    if (cut == 0) return null;
                    if (cut > 0) usRest = usRest.Substring(0, cut);
                    if (usRest.Length >= 1 && usRest.Length <= 6)
                        return "us" + usRest.ToUpperInvariant();
                }
                return null;
            }

            if (AllDigits(s))
            {
                if (s.Length <= 5) return "hk" + s.PadLeft(5, '0');
                if (s.Length == 6)
                {
                    char c = s[0];
                    if (s.StartsWith("92", StringComparison.Ordinal)) return "bj" + s;
                    if (c == '6' || c == '9' || c == '5') return "sh" + s;
                    if (c == '0' || c == '1' || c == '2' || c == '3') return "sz" + s;
                    if (c == '4' || c == '8') return "bj" + s;
                }
            }
            return null;
        }

        private static bool AllDigits(string s)
        {
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        private static readonly char[] CodeSeparators = { '.', '_' };

        public static Quote FetchOne(string code)
        {
            string normalized = NormalizeCode(code);
            if (normalized == null) return null;
            List<StockItem> items = new List<StockItem>();
            items.Add(new StockItem(normalized, ""));
            QuoteBatch batch = Fetch(items);
            return batch.Quotes.Count > 0 ? batch.Quotes[0] : null;
        }

        /// <summary>
        /// Looks up A-share, index and Beijing exchange symbols by code, Chinese
        /// name or pinyin through Tencent's smartbox endpoint.
        /// </summary>
        public static List<StockSuggestion> Search(string query, int maxResults)
        {
            List<StockSuggestion> results = new List<StockSuggestion>();
            if (query == null) return results;
            string q = query.Trim();
            if (q.Length == 0) return results;

            // Bare short numbers mean HK codes on direct entry (700 -> hk00700), so pad
            // the search the same way; otherwise smartbox ranks the A-share stock that
            // shares the padded code higher (2828 -> sz002828) and the HK one is lost.
            if (AllDigits(q) && q.Length <= 5) q = q.PadLeft(5, '0');

            string path = "ifzqgtimg/appstock/smartbox/search/get?q=" + Uri.EscapeDataString(q);
            string body;
            try
            {
                body = DownloadOne("https://proxy.finance.qq.com/" + path);
            }
            catch
            {
                try { body = DownloadOne("http://proxy.finance.qq.com/" + path); }
                catch { return results; }
            }
            ParseSearch(body, results, Math.Max(1, maxResults));
            return results;
        }

        private static void ParseSearch(string json, List<StockSuggestion> into, int maxResults)
        {
            if (string.IsNullOrEmpty(json)) return;
            int key = json.IndexOf("\"stock\":", StringComparison.Ordinal);
            if (key < 0) return;
            int i = json.IndexOf('[', key);
            if (i < 0) return;
            i++;

            List<string> fields = new List<string>();
            while (i < json.Length && into.Count < maxResults)
            {
                if (json[i] == ']') break;
                if (json[i] != '[') { i++; continue; }

                fields.Clear();
                i = ReadStringArray(json, i + 1, fields);
                if (fields.Count < 5) continue;

                string market = fields[0].Trim().ToLowerInvariant();
                string digits = fields[1].Trim();
                string name = fields[2].Trim();
                string type = fields[4].Trim();
                if (!IsTradable(market, type)) continue;

                // jj-market funds have no market prefix; the 6-digit rules decide sh/sz.
                string code = NormalizeCode(market == "jj" ? digits : market + digits);
                if (code == null || name.Length == 0) continue;
                if (FindSuggestion(into, code) != null) continue;
                into.Add(new StockSuggestion(code, name));
            }
        }

        private static StockSuggestion FindSuggestion(List<StockSuggestion> list, string code)
        {
            foreach (StockSuggestion s in list)
                if (string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        private static bool IsTradable(string market, string type)
        {
            if (market == "sh" || market == "sz")
                return type == "ZS" || type == "ETF" || type == "LOF" ||
                       type.StartsWith("GP-A", StringComparison.Ordinal);
            if (market == "bj")
                return type == "GP" || type == "ZS" || type.StartsWith("GP-A", StringComparison.Ordinal);
            if (market == "hk")
                return type == "GP" || type == "ZS" || type == "GP-ETF" || type == "GP-FUND";
            // jj covers both exchange-traded LOFs and OTC funds; only LOF has realtime quotes.
            if (market == "jj")
                return type == "LOF";
            if (market == "us")
                return type == "GP" || type == "GP-ETF" || type == "ZS";
            return false;
        }

        /// <summary>Reads one JSON array of strings, unescaping \uXXXX sequences.</summary>
        private static int ReadStringArray(string json, int i, List<string> fields)
        {
            while (i < json.Length)
            {
                char c = json[i];
                if (c == ']') return i + 1;
                if (c != '"') { i++; continue; }

                StringBuilder sb = new StringBuilder();
                i++;
                while (i < json.Length)
                {
                    char ch = json[i++];
                    if (ch == '"') break;
                    if (ch != '\\' || i >= json.Length)
                    {
                        sb.Append(ch);
                        continue;
                    }

                    char esc = json[i++];
                    if (esc == 'u' && i + 4 <= json.Length)
                    {
                        int code;
                        if (int.TryParse(json.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            sb.Append((char)code);
                        i += 4;
                    }
                    else if (esc == 'n') sb.Append('\n');
                    else if (esc == 'r') sb.Append('\r');
                    else if (esc == 't') sb.Append('\t');
                    else sb.Append(esc);
                }
                fields.Add(sb.ToString());
            }
            return i;
        }

        /// <summary>
        /// Today's one-minute price series from Tencent's minute endpoint. Beijing-exchange
        /// symbols return a single tick or nothing here; callers treat that as "no chart".
        /// </summary>
        public static IntradaySeries FetchIntraday(string code)
        {
            string normalized = NormalizeCode(code);
            if (normalized == null) return null;

            string path = "appstock/app/minute/query?code=" + normalized +
                "&_=" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture);
            string body;
            try
            {
                body = DownloadOne("https://web.ifzq.gtimg.cn/" + path);
            }
            catch
            {
                body = DownloadOne("http://web.ifzq.gtimg.cn/" + path);
            }
            return ParseIntraday(body);
        }

        private static IntradaySeries ParseIntraday(string json)
        {
            IntradaySeries series = new IntradaySeries();
            if (string.IsNullOrEmpty(json)) return series;

            int key = json.IndexOf("\"data\":[", StringComparison.Ordinal);
            if (key < 0) return series;
            int i = json.IndexOf('[', key);
            if (i < 0) return series;

            // Each entry: "HHMM price cumulativeVolume ..." — one per traded minute.
            List<string> rows = new List<string>();
            ReadStringArray(json, i + 1, rows);
            foreach (string row in rows)
            {
                string[] parts = row.Split(' ');
                if (parts.Length < 2) continue;
                double price = ParseDouble(parts[1]);
                if (price > 0) series.Prices.Add(price);
            }

            int dateKey = json.IndexOf("\"date\":\"", StringComparison.Ordinal);
            if (dateKey >= 0)
            {
                int start = dateKey + 8;
                int end = json.IndexOf('"', start);
                if (end > start) series.Date = json.Substring(start, end - start);
            }
            return series;
        }

        public static QuoteBatch Fetch(IList<StockItem> stocks)
        {
            QuoteBatch batch = new QuoteBatch();
            batch.FetchedAt = MarketClock.Now;
            if (stocks == null || stocks.Count == 0) return batch;

            List<string> codes = new List<string>();
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StockItem item in stocks)
            {
                string code = NormalizeCode(item.Code);
                if (code == null || codes.Contains(code)) continue;
                codes.Add(code);
                if (!string.IsNullOrEmpty(item.Name)) names[code] = item.Name;
            }
            if (codes.Count == 0) return batch;

            string body;
            try
            {
                body = Download(string.Join(",", codes.ToArray()));
            }
            catch (Exception ex)
            {
                batch.Error = FriendlyError(ex);
                return batch;
            }

            Parse(body, names, batch);
            if (batch.Quotes.Count == 0 && batch.Error == null)
                batch.Error = "未获取到行情数据";
            return batch;
        }

        private static string FriendlyError(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            string msg = e.Message ?? "";
            if (msg.Length > 40) msg = msg.Substring(0, 40);
            return "网络连接失败 (" + msg + ")";
        }

        private static string Download(string codes)
        {
            string query = codes + "&_=" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture);
            string url = "https://qt.gtimg.cn/q=" + query;
            Exception last = null;

            bool tryHttps;
            lock (HttpsLock) tryHttps = _httpsWorks || DateTime.Now >= _httpsRetryAfter;

            if (tryHttps)
            {
                try
                {
                    string body = DownloadOne(url);
                    lock (HttpsLock) _httpsWorks = true;
                    return body;
                }
                catch (Exception ex)
                {
                    last = ex;
                    lock (HttpsLock)
                    {
                        _httpsWorks = false;
                        _httpsRetryAfter = DateTime.Now.AddMinutes(10);
                    }
                }
            }

            try
            {
                return DownloadOne("http://qt.gtimg.cn/q=" + query);
            }
            catch (Exception ex)
            {
                throw new Exception("行情连接失败", last ?? ex);
            }
        }

        private static string DownloadOne(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            req.Referer = "https://gu.qq.com/";
            req.Accept = "*/*";
            req.Timeout = 7000;
            req.ReadWriteTimeout = 7000;
            req.KeepAlive = false;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (WebResponse resp = req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                return Encoding.GetEncoding("GB18030").GetString(ms.ToArray());
            }
        }

        private static void Parse(string text, Dictionary<string, string> fallbackNames, QuoteBatch batch)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] records = text.Split(new char[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            DateTime today = MarketClock.Now.Date;
            DateTime etToday = MarketClock.NowEastern.Date;

            foreach (string rawRecord in records)
            {
                string record = rawRecord.Trim();
                if (record.Length == 0) continue;
                int eq = record.IndexOf('=');
                if (eq <= 2) continue;
                string key = record.Substring(0, eq).Trim();
                if (!key.StartsWith("v_", StringComparison.Ordinal)) continue;
                string code = NormalizeCode(key.Substring(2));
                if (code == null) continue;

                string value = record.Substring(eq + 1).Trim();
                if (value.StartsWith("\"")) value = value.Substring(1);
                int quoteEnd = value.LastIndexOf('"');
                if (quoteEnd >= 0) value = value.Substring(0, quoteEnd);
                if (value.Length < 20) continue;

                string[] f = value.Split('~');
                if (f.Length < 33) continue;

                Quote q = new Quote();
                q.Code = code;
                q.Name = f[1].Trim();
                q.Price = ParseDouble(f[3]);
                q.PrevClose = ParseDouble(f[4]);
                q.Open = ParseDouble(f[5]);
                q.Change = ParseDouble(f[31]);
                q.ChangePercent = ParseDouble(f[32]);
                if (f.Length > 33) q.High = ParseDouble(f[33]);
                if (f.Length > 34) q.Low = ParseDouble(f[34]);
                if (f.Length > 36) q.Volume = ParseDouble(f[36]);
                if (f.Length > 37) q.Amount = ParseDouble(f[37]);

                if (f.Length > 30)
                {
                    string stamp = f[30].Trim();
                    DateTime dt = DateTime.MinValue;
                    bool stamped;
                    if (stamp.IndexOf('/') > 0)
                    {
                        // HK endpoint: "2026/11/5 9:33:30"
                        stamped = DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out dt);
                    }
                    else if (stamp.IndexOf('-') > 0)
                    {
                        // US endpoint: "2026-11-04 16:00:01" (US Eastern local time)
                        stamped = DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out dt);
                    }
                    else if (stamp.Length >= 14)
                    {
                        // A-share endpoint: compact yyyyMMddHHmmss
                        stamped = DateTime.TryParseExact(stamp.Substring(0, 14), "yyyyMMddHHmmss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                    }
                    else
                    {
                        stamped = false;
                    }
                    if (stamped)
                    {
                        q.Time = dt;
                        q.HasTime = true;
                    }
                }

                if (q.PrevClose > 0)
                {
                    q.Change = q.Price - q.PrevClose;
                    q.ChangePercent = q.Change / q.PrevClose * 100.0;
                }

                if (q.Name.Length == 0 && fallbackNames.ContainsKey(code)) q.Name = fallbackNames[code];
                if (q.Name.Length == 0) continue;

                // US stamps are Eastern-time, so "today" for freshness follows that market.
                bool isUS = code.StartsWith("us", StringComparison.Ordinal);
                DateTime marketToday = isUS ? etToday : today;
                if (q.HasTime && q.Time.Date == marketToday) batch.HasTodayData = true;
                if (q.HasTime && q.Time > batch.LatestQuoteTime) batch.LatestQuoteTime = q.Time;
                if (isUS && q.HasTime) q.SessionTag = MarketClock.USSessionTag(q.Time);
                batch.Quotes.Add(q);
            }
        }

        private static double ParseDouble(string s)
        {
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }
    }
}
