using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AStockTicker
{
    /// <summary>
    /// A transparent, topmost strip aligned with the empty area of the Windows 11
    /// taskbar. It shows two watch-list stocks on two rows (name + price on each,
    /// price tinted by the day's move) and rotates the pair through the list.
    /// </summary>
    public class TickerForm : Form
    {
        // ---- Win32 ----
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX, CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER Header;
            public int Colors;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dstDc, ref POINT dst,
            ref SIZE size, IntPtr srcDc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, uint flags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint ULW_ALPHA = 0x00000002;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // ---- State ----
        private readonly AppConfig _config;
        private readonly System.Windows.Forms.Timer _timer;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _showItem;
        private ToolStripMenuItem _monoItem;
        private ToolStripMenuItem _privacyItem;
        private ToolStripMenuItem _autoStartItem;
        private readonly List<ToolStripMenuItem> _fontItems = new List<ToolStripMenuItem>();
        private readonly List<ToolStripMenuItem> _frostItems = new List<ToolStripMenuItem>();

        private SessionInfo _session;
        private QuoteBatch _lastBatch;
        private readonly List<Quote> _displayQuotes = new List<Quote>();
        private string _lastError;
        private bool _fetching;
        private DateTime _nextFetch = DateTime.MinValue;
        private bool _manualOverride;
        private bool _manualVisible;
        private bool _holidayDetected;
        private DateTime _lastDate = DateTime.MinValue;

        private class IntradayCache { public IntradaySeries Series; public DateTime At; }
        private readonly Dictionary<string, IntradayCache> _intraday = new Dictionary<string, IntradayCache>();
        private readonly List<string> _intradayPending = new List<string>();
        private static readonly TimeSpan IntradayTtl = TimeSpan.FromSeconds(60);

        private const int FadeSteps = 8;
        private System.Windows.Forms.Timer _fadeTimer;
        private bool _fading;
        private int _fadeStep;
        private bool _fadeToClear;
        private bool _lastObscured;
        private Bitmap _fadeClear;
        private Bitmap _fadeFrost;

        private float _dpi = 1f;
        private int _startIndex;
        private DateTime _lastCarousel = DateTime.MinValue;
        private bool _hover;
        private bool _menuOpen;
        private bool _menuFromStrip;
        private System.Windows.Forms.Timer _hoverTimer;
        private Rectangle _widgetRect;
        private bool _hasPlacement;
        private SessionKind _trayKind = (SessionKind)(-1);
        private DateTime _lastTopMost = DateTime.MinValue;
        private bool _readyToShow;
        private bool _disposed;
        private bool _exiting;

        public TickerForm(AppConfig config)
        {
            _config = config;
            _dpi = DetectDpi();

            Text = "A股行情条";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.Opaque, true);

            _session = MarketClock.GetCombined(MarketClock.Now, _config.HasHongKong(), _config.HasUS());
            _lastDate = _session.Now.Date;

            // Debug affordance: "--show" keeps the strip visible with fresh quotes even when closed.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--show") >= 0)
            {
                _manualOverride = true;
                _manualVisible = true;
            }

            BuildMenu();
            _tray = new NotifyIcon();
            _tray.Icon = TrayIconFactory.Get(_session.Kind);
            _tray.Text = "A股行情条";
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ToggleVisible(); };

            ContextMenuStrip = _menu;

            BuildDisplayQuotes();
            IntPtr handle = Handle;
            UpdatePlacement(true);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 500;
            _timer.Tick += OnTick;
            _timer.Start();

            _hoverTimer = new System.Windows.Forms.Timer();
            _hoverTimer.Interval = 120;
            _hoverTimer.Tick += delegate { PollHover(); };
            _hoverTimer.Start();

            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = 30;
            _fadeTimer.Tick += delegate { FadeTick(); };

            BeginFetch(true);
            TryHookSystemEvents();
        }

        private float DetectDpi()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(0.75f, Math.Min(3f, g.DpiX / 96f));
            }
            catch { return 1f; }
        }

        private float FontFactor()
        {
            return _dpi * (0.8f + 0.02f * _config.FontSize);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_readyToShow)
            {
                _readyToShow = true;
                value = ShouldShowNow() && TaskbarAvailable();
                if (value)
                {
                    UpdatePlacement(true);
                    RenderLayered();
                }
            }
            if (value) _lastCarousel = MarketClock.Now;
            else
            {
                _hover = false;
                StopFade();
            }
            base.SetVisibleCore(value);
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Microsoft YaHei UI", 9F);

            _showItem = new ToolStripMenuItem("显示行情条");
            _showItem.Click += delegate { ToggleVisible(); };
            _menu.Items.Add(_showItem);

            ToolStripMenuItem next = new ToolStripMenuItem("下一组");
            next.Click += delegate { NextStock(true); };
            _menu.Items.Add(next);

            ToolStripMenuItem fontMenu = new ToolStripMenuItem("字体大小");
            for (int size = 8; size <= 14; size++)
            {
                int chosen = size;
                ToolStripMenuItem item = new ToolStripMenuItem(size.ToString());
                item.Tag = size;
                item.Click += delegate { SetFontSize(chosen); };
                fontMenu.DropDownItems.Add(item);
                _fontItems.Add(item);
            }
            fontMenu.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem item in _fontItems)
                    item.Checked = Convert.ToInt32(item.Tag) == _config.FontSize;
            };
            _menu.Items.Add(fontMenu);

            _monoItem = new ToolStripMenuItem("黑白显示");
            _monoItem.Click += delegate { ToggleMono(); };
            _menu.Items.Add(_monoItem);

            _privacyItem = new ToolStripMenuItem("隐私模式");
            _privacyItem.Click += delegate { TogglePrivacy(); };
            _menu.Items.Add(_privacyItem);

            ToolStripMenuItem frostMenu = new ToolStripMenuItem("模糊浓度");
            for (int lvl = 1; lvl <= 3; lvl++)
            {
                int chosen = lvl;
                ToolStripMenuItem item = new ToolStripMenuItem(lvl == 1 ? "淡" : (lvl == 2 ? "中" : "浓"));
                item.Tag = lvl;
                item.Click += delegate { SetFrostLevel(chosen); };
                frostMenu.DropDownItems.Add(item);
                _frostItems.Add(item);
            }
            frostMenu.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem item in _frostItems)
                    item.Checked = Convert.ToInt32(item.Tag) == _config.FrostLevel;
            };
            _menu.Items.Add(frostMenu);

            ToolStripMenuItem refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { BeginFetch(true); };
            _menu.Items.Add(refresh);

            ToolStripMenuItem settings = new ToolStripMenuItem("自选股设置...");
            settings.Click += delegate { OpenSettings(); };
            _menu.Items.Add(settings);

            ToolStripMenuItem openConfig = new ToolStripMenuItem("打开配置文件");
            openConfig.Click += delegate { OpenConfigFile(); };
            _menu.Items.Add(openConfig);

            _menu.Items.Add(new ToolStripSeparator());

            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.Click += delegate { ToggleAutoStart(); };
            _menu.Items.Add(_autoStartItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem about = new ToolStripMenuItem("关于");
            about.Click += delegate { ShowAbout(); };
            _menu.Items.Add(about);

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };
            _menu.Items.Add(exit);

            _menu.Opening += delegate
            {
                _menuFromStrip = _widgetRect.Contains(Cursor.Position);
                _showItem.Checked = Visible;
                _monoItem.Checked = _config.MonoColor;
                _privacyItem.Checked = _config.PrivacyMode;
                _autoStartItem.Checked = StartupManager.IsEnabled();
            };
            _menu.Opened += delegate { _menuOpen = true; };
            _menu.Closed += delegate
            {
                _menuOpen = false;
                _lastCarousel = MarketClock.Now;
                if (_config.PrivacyMode && Visible) StartFade(_hover);
            };
        }

        private void TryHookSystemEvents()
        {
            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnSystemChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch { }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_disposed) return;
            try { if (Visible) RenderLayered(); }
            catch { }
        }

        private void OnSystemChanged(object sender, EventArgs e)
        {
            if (_disposed) return;
            try
            {
                _dpi = DetectDpi();
                UpdatePlacement(true);
                if (Visible) RenderLayered();
            }
            catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed) return;
            DateTime now = MarketClock.Now;
            SessionInfo current = MarketClock.GetCombined(now, _config.HasHongKong(), _config.HasUS());
            bool changed = _session == null || _session.Key != current.Key;
            if (changed)
            {
                _session = current;
                _manualOverride = false;
                _manualVisible = false;
                if (current.CanShow) _nextFetch = DateTime.MinValue;
            }
            else
            {
                _session = current;
            }

            if (now.Date != _lastDate)
            {
                _lastDate = now.Date;
                _holidayDetected = false;
            }

            UpdateVisibility(now);
            UpdateTray();
            if (Visible) EnsureIntraday();

            // In privacy mode hovering is the reading gesture, so rotation must
            // continue; the unhovered state is frost anyway, where rotation is
            // invisible. Normal mode keeps the documented hover-pause.
            if (Visible && (_config.PrivacyMode || !_hover) && !_menuOpen && _displayQuotes.Count > 2 &&
                (now - _lastCarousel).TotalSeconds >= Math.Max(2, _config.CarouselSeconds))
            {
                NextStock(false);
            }

            if (now >= _nextFetch && ShouldFetch()) BeginFetch(false);
            else if (!ShouldFetch()) _nextFetch = now.AddSeconds(30);
        }

        private void SetFontSize(int size)
        {
            _config.FontSize = Math.Max(8, Math.Min(14, size));
            try { _config.Save(); } catch { }
            UpdatePlacement(true);
            UpdateVisibility(MarketClock.Now);
            if (Visible) RenderLayered();
        }

        private void NextStock(bool force)
        {
            if (_displayQuotes.Count == 0) return;
            if (_displayQuotes.Count <= 2 && !force) return;
            _startIndex = (_startIndex + 1) % _displayQuotes.Count;
            _lastCarousel = MarketClock.Now;
            if (Visible) RenderLayered();
        }

        private SessionInfo EffectiveSession()
        {
            if (!_holidayDetected || _session == null) return _session;
            SessionInfo s = new SessionInfo();
            s.Kind = SessionKind.Closed;
            s.Label = "休市";
            s.Detail = "今日休市";
            s.CanShow = false;
            s.IsTradingDay = false;
            s.Now = _session.Now;
            return s;
        }

        private bool ShouldShowNow()
        {
            if (_manualOverride) return _manualVisible;
            if (!_config.HideWhenClosed) return true;
            SessionInfo s = EffectiveSession();
            return s != null && s.CanShow;
        }

        private bool ShouldFetch()
        {
            SessionInfo s = EffectiveSession();
            if (s != null && s.CanShow) return true;
            return _manualOverride && _manualVisible;
        }

        private void UpdateVisibility(DateTime now)
        {
            bool available = UpdatePlacement(false);
            bool show = ShouldShowNow() && available;
            if (show)
            {
                if (!Visible)
                {
                    RenderLayered();
                    Show();
                    _lastCarousel = now;
                    _lastTopMost = DateTime.Now;
                }
                else if ((DateTime.Now - _lastTopMost).TotalSeconds >= 2)
                {
                    // Keep the strip above the taskbar even after the taskbar is clicked.
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    _lastTopMost = DateTime.Now;
                }
            }
            else if (Visible)
            {
                Hide();
            }
        }

        /// <summary>Finds the primary taskbar and positions the transparent strip left of the tray.</summary>
        private bool UpdatePlacement(bool forceRender)
        {
            Rectangle tray;
            Rectangle notify;
            if (!GetTaskbarGeometry(out tray, out notify))
            {
                _hasPlacement = false;
                return false;
            }

            float factor = FontFactor();
            int width = MeasureStripWidth(tray.Height);
            int height = tray.Height;
            int gap = (int)Math.Round(12 * factor);
            int minX = tray.Left + (int)Math.Round(70 * factor);
            int x = notify.Left - width - gap;
            if (x < minX) x = minX;
            int maxRight = notify.Left - (int)Math.Round(4 * factor);
            if (x + width > maxRight) width = Math.Max((int)Math.Round(120 * factor), maxRight - x);
            Rectangle rect = new Rectangle(x, tray.Top, width, height);

            bool geometryChanged = !_hasPlacement || rect != _widgetRect;
            _widgetRect = rect;
            _hasPlacement = true;

            if (geometryChanged)
                SetWindowPos(Handle, HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
                    SWP_NOACTIVATE | SWP_NOOWNERZORDER);

            if (geometryChanged || forceRender || !Visible)
                RenderLayered();
            return true;
        }

        private bool TaskbarAvailable()
        {
            Rectangle tray;
            Rectangle notify;
            return GetTaskbarGeometry(out tray, out notify);
        }

        private bool GetTaskbarGeometry(out Rectangle tray, out Rectangle notify)
        {
            tray = Rectangle.Empty;
            notify = Rectangle.Empty;
            IntPtr hTray = FindWindow("Shell_TrayWnd", null);
            if (hTray == IntPtr.Zero || !IsWindowVisible(hTray)) return false;

            RECT tr;
            if (!GetWindowRect(hTray, out tr)) return false;
            tray = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
            if (tray.Width < 200 || tray.Height < 24) return false;

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            // Auto-hidden taskbar is moved below the screen.
            if (tray.Top >= screen.Bottom - 2 || tray.Bottom > screen.Bottom + 4) return false;

            IntPtr hNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (hNotify != IntPtr.Zero)
            {
                RECT nr;
                if (GetWindowRect(hNotify, out nr))
                    notify = Rectangle.FromLTRB(nr.Left, nr.Top, nr.Right, nr.Bottom);
            }
            if (notify.Width < 80)
                notify = new Rectangle(tray.Right - 400, tray.Top, 400, tray.Height);

            return true;
        }

        private void BuildDisplayQuotes()
        {
            _displayQuotes.Clear();
            foreach (StockItem stock in _config.Stocks)
            {
                Quote q = _lastBatch != null ? _lastBatch.Find(stock.Code) : null;
                if (q == null)
                {
                    q = new Quote();
                    q.Code = stock.Code;
                    q.Name = stock.Name;
                    q.Price = 0;
                }
                _displayQuotes.Add(q);
            }
            if (_displayQuotes.Count == 0)
                _startIndex = 0;
            else if (_startIndex >= _displayQuotes.Count)
                _startIndex = 0;
        }

        /// <summary>The one or two quotes currently on the strip, top row first.</summary>
        private List<Quote> DisplayedQuotes()
        {
            List<Quote> list = new List<Quote>();
            if (_displayQuotes.Count == 0) return list;
            int start = _startIndex % _displayQuotes.Count;
            list.Add(_displayQuotes[start]);
            if (_displayQuotes.Count > 1)
                list.Add(_displayQuotes[(start + 1) % _displayQuotes.Count]);
            return list;
        }

        private bool IsLightTaskbar()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");
                        if (value != null) return Convert.ToInt32(value) != 0;
                    }
                }
            }
            catch { }
            return true;
        }

        private static Color PriceColor(Quote q, bool light)
        {
            if (q == null || q.Price <= 0.0001)
                return light ? Color.FromArgb(75, 75, 78) : Color.FromArgb(200, 200, 200);
            if (q.ChangePercent > 0.0001)
                return light ? Color.FromArgb(196, 32, 38) : Color.FromArgb(255, 92, 92);
            if (q.ChangePercent < -0.0001)
                return light ? Color.FromArgb(0, 108, 82) : Color.FromArgb(0, 220, 142);
            return light ? Color.FromArgb(60, 60, 62) : Color.FromArgb(220, 220, 220);
        }

        /// <summary>Keeps today's minute series cached per displayed stock, refreshing every minute.</summary>
        private void EnsureIntraday()
        {
            if (!Visible || _disposed) return;
            foreach (Quote q in DisplayedQuotes())
            {
                if (q == null || q.Price <= 0.0001) continue;
                IntradayCache c;
                if (_intraday.TryGetValue(q.Code, out c) && c != null && MarketClock.Now - c.At < IntradayTtl) continue;
                if (_intradayPending.Contains(q.Code)) continue;

                _intradayPending.Add(q.Code);
                string code = q.Code;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    IntradaySeries series = null;
                    try { series = QuoteService.FetchIntraday(code); }
                    catch { }
                    try
                    {
                        if (IsHandleCreated && !_disposed)
                        {
                            BeginInvoke(new Action(delegate { OnIntradayFetched(code, series); }));
                            return;
                        }
                    }
                    catch { }
                    _intradayPending.Remove(code);
                });
            }
        }

        private void OnIntradayFetched(string code, IntradaySeries series)
        {
            _intradayPending.Remove(code);
            if (_disposed) return;
            IntradayCache c = new IntradayCache();
            c.Series = series;
            c.At = MarketClock.Now;
            _intraday[code] = c;
            if (Visible && IntradaySeries(code) != null) RenderLayered();
        }

        private List<double> IntradaySeries(string code)
        {
            IntradayCache c;
            if (!_intraday.TryGetValue(code, out c) || c == null || c.Series == null) return null;
            List<double> prices = c.Series.Prices;
            return (prices != null && prices.Count >= 2) ? prices : null;
        }

        /// <summary>Mini intraday line over a fixed 240-minute axis with a dotted previous-close baseline.</summary>
        private static void DrawIntraday(Graphics g, List<double> prices, double baseline,
            float x, float y, float width, float height, Color color, Color baselineColor)
        {
            double min = baseline > 0 ? baseline : double.MaxValue;
            double max = baseline > 0 ? baseline : double.MinValue;
            foreach (double v in prices)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double span = max - min;
            float innerH = height - 3f;
            float wSpan = Math.Max(1f, width - 2f);

            if (baseline > 0)
            {
                float bt = span < 0.000001 ? 0.5f : (float)((baseline - min) / span);
                float by = y + 1.5f + (1f - bt) * innerH;
                using (Pen basePen = new Pen(baselineColor, 1f))
                {
                    basePen.DashStyle = DashStyle.Dot;
                    g.DrawLine(basePen, x, by, x + wSpan, by);
                }
            }

            using (Pen pen = new Pen(color, 1.3f))
            {
                pen.LineJoin = LineJoin.Round;
                PointF[] pts = new PointF[prices.Count];
                for (int i = 0; i < prices.Count; i++)
                {
                    float t = prices.Count > 1 ? (float)i / 240f : 0f;
                    if (t > 1f) t = 1f;
                    float vt = span < 0.000001 ? 0.5f : (float)((prices[i] - min) / span);
                    pts[i] = new PointF(x + t * wSpan, y + 1.5f + (1f - vt) * innerH);
                }
                g.DrawLines(pen, pts);
            }
        }

        private void GetTextMetrics(int height, out float nameSize, out float priceSize,
            out float pad, out float rowHeight, out float gap)
        {
            bool twoRows = _displayQuotes.Count > 1;
            float fontScale = 0.7f + 0.03f * _config.FontSize;
            pad = Math.Max(3f, height * 0.06f);
            rowHeight = twoRows ? height / 2f : height;
            nameSize = (twoRows ? rowHeight * 0.48f : rowHeight * 0.30f) * fontScale;
            priceSize = nameSize;
            gap = Math.Max(4f, height * 0.18f);
        }

        /// <summary>Width needed for name, price, percent and the mini trend chart, sized for the widest entry.
        /// US watchlists reserve a fixed slot for the pre/after-hours tag so rows do not reflow.</summary>
        private int MeasureStripWidth(int height)
        {
            float nameSize, priceSize, pad, rowHeight, gap;
            GetTextMetrics(height, out nameSize, out priceSize, out pad, out rowHeight, out gap);
            float pctSize = priceSize * 0.85f;
            float pctGap = Math.Max(3f, height * 0.08f);
            float chartW = Math.Max(48f, Math.Min(96f, height * 1.1f));
            bool anyTag = AnySessionTag(_displayQuotes);

            float maxName = 0f;
            float maxPrice = 0f;
            float maxPct = 0f;
            float tagW = 0f;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            using (Font nameFont = new Font("Microsoft YaHei UI", nameSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font priceFont = new Font("Microsoft YaHei UI", priceSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font pctFont = new Font("Microsoft YaHei UI", pctSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                if (_displayQuotes.Count == 0)
                {
                    maxName = g.MeasureString("未添加自选股", nameFont, new PointF(0f, 0f), sf).Width;
                    maxPrice = g.MeasureString("--", priceFont, new PointF(0f, 0f), sf).Width;
                }
                foreach (Quote q in _displayQuotes)
                {
                    maxName = Math.Max(maxName, g.MeasureString(q.Name, nameFont, new PointF(0f, 0f), sf).Width);
                    maxPrice = Math.Max(maxPrice, g.MeasureString(q.PriceText, priceFont, new PointF(0f, 0f), sf).Width);
                    maxPct = Math.Max(maxPct, g.MeasureString(q.ChangeText, pctFont, new PointF(0f, 0f), sf).Width);
                }
                if (anyTag) tagW = MeasureTagWidth(g, pctSize);
            }
            return (int)Math.Ceiling(pad * 2f + maxName + gap + maxPrice + pctGap + maxPct +
                (anyTag ? pctGap + tagW : 0f) + chartW) + 4;
        }

        private static bool AnySessionTag(List<Quote> quotes)
        {
            foreach (Quote q in quotes)
                if (q != null && q.Code != null &&
                    q.Code.StartsWith("us", StringComparison.Ordinal)) return true;
            return false;
        }

        private static float TagFontSize(float pctSize)
        {
            return Math.Max(7f, pctSize * 0.62f);
        }

        private static float MeasureTagWidth(Graphics g, float pctSize)
        {
            using (Font tagFont = new Font("Microsoft YaHei UI", TagFontSize(pctSize), FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                return g.MeasureString("盘后", tagFont, new PointF(0f, 0f), sf).Width + 8f;
            }
        }

        /// <summary>Soft pill tag ("盘前"/"盘后") next to the chart for US quotes stamped outside regular hours.</summary>
        private void DrawSessionTag(Graphics g, Quote q, float x, float w, float rowTop, float rowHeight,
            bool light, float pctSize)
        {
            if (q == null || string.IsNullOrEmpty(q.SessionTag) || w < 12f) return;
            float fontSize = TagFontSize(pctSize);
            float tagH = fontSize + 6f;
            float tagY = rowTop + (rowHeight - tagH) / 2f;
            RectangleF rect = new RectangleF(x, tagY, w, tagH);
            Color fill = light ? Color.FromArgb(22, 28, 28, 30) : Color.FromArgb(32, 245, 245, 245);
            Color text = light ? Color.FromArgb(120, 120, 126) : Color.FromArgb(208, 208, 214);
            using (Font tagFont = new Font("Microsoft YaHei UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (GraphicsPath path = RoundedRectPath(rect, tagH / 2f))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (SolidBrush textBrush = new SolidBrush(text))
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(fillBrush, path);
                g.DrawString(q.SessionTag, tagFont, textBrush, rect, sf);
            }
        }

        private static GraphicsPath RoundedRectPath(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void RenderLayered()
        {
            if (_fading) return;
            PresentFrame(DrawStateFrame);
        }

        private void DrawStateFrame(Graphics g, int w, int h)
        {
            bool obscured = _config.PrivacyMode && !_hover && !(_menuOpen && _menuFromStrip);
            _lastObscured = obscured;
            if (obscured) DrawFrost(g, w, h);
            else RenderContent(g, w, h);
        }

        private void DrawFrost(Graphics g, int w, int h)
        {
            int div = _config.FrostLevel == 1 ? 8 : (_config.FrostLevel == 3 ? 16 : 12);
            float density = _config.FrostLevel == 1 ? 0.62f : (_config.FrostLevel == 3 ? 1.45f : 1f);
            using (Bitmap content = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics cg = Graphics.FromImage(content))
            {
                RenderContent(cg, w, h);
                BlurBitmap(content, div);
                FeatherEdges(content, density);
                g.DrawImage(content, 0, 0, w, h);
            }
        }

        /// <summary>Renders one frame through the layered-window DIB and presents it.</summary>
        private void PresentFrame(DrawHandler draw)
        {
            if (!_hasPlacement || _widgetRect.Width < 40 || _widgetRect.Height < 20) return;
            int w = _widgetRect.Width;
            int h = _widgetRect.Height;

            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.Header.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.Header.biWidth = w;
            bmi.Header.biHeight = -h;
            bmi.Header.biPlanes = 1;
            bmi.Header.biBitCount = 32;
            bmi.Header.biCompression = 0;

            IntPtr bits;
            IntPtr bmp = CreateDIBSection(mem, ref bmi, 0, out bits, IntPtr.Zero, 0);
            IntPtr old = SelectObject(mem, bmp);

            try
            {
                using (Graphics g = Graphics.FromHdc(mem))
                {
                    g.Clear(Color.Transparent);
                    draw(g, w, h);
                }

                POINT dst = new POINT();
                dst.X = _widgetRect.Left;
                dst.Y = _widgetRect.Top;
                SIZE size = new SIZE();
                size.CX = w;
                size.CY = h;
                POINT src = new POINT();
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1;
                if (!UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA))
                    DebugLog.Write("UpdateLayeredWindow failed");
            }
            catch (Exception ex)
            {
                DebugLog.Write("render error " + ex.Message);
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private delegate void DrawHandler(Graphics g, int w, int h);

        /// <summary>Animates the frost/clear transition so the glass dissolves instead of snapping.</summary>
        private void StartFade(bool toClear)
        {
            if (!_config.PrivacyMode) { RenderLayered(); return; }
            if (_fading && _fadeToClear == toClear) return;
            StopFade();
            if (!Visible || !_hasPlacement || _widgetRect.Width < 40 || _widgetRect.Height < 20) return;
            if (_lastObscured != toClear)
            {
                // Already showing the requested state; just make sure it is on screen.
                RenderLayered();
                return;
            }

            int w = _widgetRect.Width;
            int h = _widgetRect.Height;
            _fadeClear = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics cg = Graphics.FromImage(_fadeClear)) RenderContent(cg, w, h);
            _fadeFrost = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics cg2 = Graphics.FromImage(_fadeFrost)) DrawFrost(cg2, w, h);
            _fadeStep = 0;
            _fadeToClear = toClear;
            _lastObscured = !toClear;
            _fading = true;
            _fadeTimer.Start();
        }

        private void FadeTick()
        {
            if (!_fading || _fadeClear == null || _fadeFrost == null) { StopFade(); return; }
            _fadeStep++;
            float t = _fadeStep / (float)FadeSteps;
            if (t >= 1f) { StopFade(); RenderLayered(); return; }
            float e = t * t * (3f - 2f * t);
            float frostAlpha = _fadeToClear ? 1f - e : e;
            Bitmap clearFrame = _fadeClear;
            Bitmap frostFrame = _fadeFrost;
            PresentFrame(delegate(Graphics g, int w, int h)
            {
                DrawWithAlpha(g, clearFrame, 1f);
                DrawWithAlpha(g, frostFrame, frostAlpha);
            });
        }

        private void StopFade()
        {
            if (_fadeTimer != null) _fadeTimer.Stop();
            _fading = false;
            if (_fadeClear != null) { _fadeClear.Dispose(); _fadeClear = null; }
            if (_fadeFrost != null) { _fadeFrost.Dispose(); _fadeFrost = null; }
        }

        private static void DrawWithAlpha(Graphics g, Image img, float alpha)
        {
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = alpha;
                ia.SetColorMatrix(cm);
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height),
                    0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>Draws name/price/pct/intraday content into g at size w×h.
        /// Fills a near-invisible 1-alpha backdrop first so the whole rect hit-tests for the mouse;
        /// otherwise only text pixels capture the cursor and enter/leave churns between glyphs.</summary>
        private void RenderContent(Graphics g, int w, int h)
        {
            g.Clear(Color.FromArgb(1, 0, 0, 0));
            bool light = IsLightTaskbar();
            Color nameColor = light ? Color.FromArgb(28, 28, 30) : Color.FromArgb(245, 245, 245);

            List<Quote> quotes = DisplayedQuotes();
            int rows = quotes.Count > 0 ? quotes.Count : 1;

            float nameSize, priceSize, pad, rowHeight, gap;
            GetTextMetrics(h, out nameSize, out priceSize, out pad, out rowHeight, out gap);

            using (Font nameFont = new Font("Microsoft YaHei UI", nameSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font priceFont = new Font("Microsoft YaHei UI", priceSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font pctFont = new Font("Microsoft YaHei UI", priceSize * 0.85f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush nameBrush = new SolidBrush(nameColor))
            using (SolidBrush priceBrush = new SolidBrush(nameColor))
            using (StringFormat sf = StringFormat.GenericTypographic)
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                float nameColumn = 0f;
                float priceColumn = 0f;
                for (int i = 0; i < rows; i++)
                {
                    string n = quotes.Count > 0 ? quotes[i].Name : "未添加自选股";
                    string p = quotes.Count > 0 ? quotes[i].PriceText : "--";
                    nameColumn = Math.Max(nameColumn,
                        g.MeasureString(n, nameFont, new PointF(0f, 0f), sf).Width);
                    priceColumn = Math.Max(priceColumn,
                        g.MeasureString(p, priceFont, new PointF(0f, 0f), sf).Width);
                }
                float pctGap = Math.Max(3f, h * 0.08f);
                float chartW = Math.Max(48f, Math.Min(96f, h * 1.1f));
                float chartH = rowHeight * 0.72f;
                float chartX = w - pad - chartW;
                float priceX = pad + (float)Math.Ceiling(nameColumn) + gap;
                float pctX = priceX + (float)Math.Ceiling(priceColumn) + pctGap;
                bool anyTag = AnySessionTag(_displayQuotes);
                float tagW = anyTag ? MeasureTagWidth(g, priceSize * 0.85f) : 0f;
                float tagX = chartX - pctGap - tagW;

                for (int i = 0; i < rows; i++)
                {
                    Quote q = quotes.Count > 0 ? quotes[i] : null;
                    string name = q != null ? q.Name : "未添加自选股";
                    string price = q != null ? q.PriceText : "--";
                    priceBrush.Color = _config.MonoColor ? nameColor : PriceColor(q, light);

                    float rowTop = i * rowHeight;
                    float nameWidth = (float)Math.Ceiling(nameColumn) + 1f;
                    bool showPct = q != null;
                    float priceWidth = showPct
                        ? Math.Max(1f, pctX - priceX - pctGap + 1f)
                        : Math.Max(1f, w - pad - priceX);

                    sf.Alignment = StringAlignment.Near;
                    g.DrawString(name, nameFont, nameBrush,
                        new RectangleF(pad, rowTop, nameWidth, rowHeight), sf);
                    g.DrawString(price, priceFont, priceBrush,
                        new RectangleF(priceX, rowTop, priceWidth, rowHeight), sf);
                    if (q != null)
                    {
                        float pctRight = anyTag ? tagX - pctGap : chartX - pctGap;
                        float pctWidth = Math.Max(1f, pctRight - pctX);
                        g.DrawString(q.ChangeText, pctFont, priceBrush,
                            new RectangleF(pctX, rowTop, pctWidth, rowHeight), sf);
                        DrawSessionTag(g, q, tagX, tagW, rowTop, rowHeight, light, priceSize * 0.85f);

                        List<double> series = IntradaySeries(q.Code);
                        if (series != null && chartW >= 12f && chartH >= 8f)
                        {
                            Color baselineColor = light
                                ? Color.FromArgb(140, 140, 146)
                                : Color.FromArgb(120, 120, 128);
                            DrawIntraday(g, series, q.PrevClose,
                                chartX, rowTop + (rowHeight - chartH) / 2f,
                                chartW, chartH, priceBrush.Color, baselineColor);
                        }
                    }
                }
            }
        }

        /// <summary>Frosted-glass blur: downscale hard then upscale back, so nothing stays readable.</summary>
        private static void BlurBitmap(Bitmap bmp, int div)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int sw = Math.Max(1, w / div);
            int sh = Math.Max(1, h / div);
            using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(small))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(bmp, new Rectangle(0, 0, sw, sh),
                    0, 0, w, h, GraphicsUnit.Pixel, ia);
                using (Graphics g2 = Graphics.FromImage(bmp))
                {
                    g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g2.Clear(Color.Transparent);
                    g2.DrawImage(small, new Rectangle(0, 0, w, h),
                        0, 0, sw, sh, GraphicsUnit.Pixel, ia);
                }
            }
        }

        /// <summary>Scales the frost density, then fades the alpha from the borders toward the
        /// middle so the frosted area dissolves into the taskbar instead of ending in a hard rectangle.</summary>
        private static void FeatherEdges(Bitmap bmp, float density)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int mx = Math.Max(8, w * 22 / 100);
            int my = Math.Max(5, h * 42 / 100);
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buf = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < h; y++)
                {
                    float fy = EdgeRamp(y, h, my);
                    int row = y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        int p = row + x * 4;
                        int a = (int)(buf[p + 3] * density + 0.5f);
                        if (a > 255) a = 255;
                        a = (int)(a * fy * EdgeRamp(x, w, mx) + 0.5f);
                        if (a < 1) a = 1;
                        buf[p + 3] = (byte)a;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>Smoothstep 0 at the border to 1 once deeper than margin pixels.</summary>
        private static float EdgeRamp(int i, int n, int margin)
        {
            int d = Math.Min(i, n - 1 - i);
            if (d >= margin) return 1f;
            float t = (float)d / margin;
            return t * t * (3f - 2f * t);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            List<Quote> quotes = DisplayedQuotes();
            if (quotes.Count == 0) return;
            Quote q = quotes[0];
            if (quotes.Count > 1 && PointToClient(Cursor.Position).Y >= ClientSize.Height / 2)
                q = quotes[1];
            if (q.Price <= 0) return;
            string suffix = (q.Code.StartsWith("sh000") || q.Code.StartsWith("sz399")) ? "/zs" : "/gp";
            try { Process.Start("https://gu.qq.com/" + q.Code + suffix); }
            catch { }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            if (_config.PrivacyMode && Visible) StartFade(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            // Mouse tracking is unreliable on the per-pixel-alpha layered window
            // (spurious leaves while moving between glyphs), so in privacy mode the
            // poll timer below owns the hover state.
            if (_config.PrivacyMode) return;
            _hover = false;
            _lastCarousel = MarketClock.Now;
        }

        private void PollHover()
        {
            KeepAboveTaskbar();
            if (!_config.PrivacyMode || !Visible || _menuOpen || !_hasPlacement) return;
            bool inside = _widgetRect.Contains(Cursor.Position);
            if (inside == _hover) return;
            _hover = inside;
            _lastCarousel = MarketClock.Now;
            StartFade(inside);
        }

        // Clicking the taskbar (tray icon included) activates Shell_TrayWnd, which jumps
        // above this topmost strip; recover within one poll tick instead of waiting for
        // the 2s re-assert in UpdateVisibility.
        private void KeepAboveTaskbar()
        {
            if (!Visible) return;
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            StringBuilder name = new StringBuilder(32);
            if (GetClassName(fg, name, 32) <= 0 || name.ToString() != "Shell_TrayWnd") return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
                SetFontSize(_config.FontSize + (e.Delta > 0 ? 1 : -1));
        }

        private void BeginFetch(bool force)
        {
            if (_fetching) return;
            if (!force && !ShouldFetch())
            {
                _nextFetch = DateTime.Now.AddSeconds(30);
                return;
            }
            _fetching = true;
            _nextFetch = DateTime.Now.AddSeconds(Math.Max(1, _config.RefreshSeconds));

            List<StockItem> stocks = new List<StockItem>();
            foreach (StockItem s in _config.Stocks) stocks.Add(s.Clone());

            ThreadPool.QueueUserWorkItem(delegate
            {
                QuoteBatch batch = QuoteService.Fetch(stocks);
                try
                {
                    if (IsHandleCreated && !_disposed)
                    {
                        BeginInvoke(new Action<QuoteBatch>(OnFetched), batch);
                        return;
                    }
                }
                catch { }
                _fetching = false;
            });
        }

        private void OnFetched(QuoteBatch batch)
        {
            if (_disposed) return;
            _fetching = false;
            if (batch == null) return;

            if (batch.Error != null && batch.Quotes.Count == 0)
            {
                _lastError = batch.Error;
            }
            else
            {
                _lastError = null;
                _lastBatch = batch;
                DetectHoliday(batch);
            }

            BuildDisplayQuotes();
            UpdateTray();
            UpdateVisibility(MarketClock.Now);
            if (Visible) RenderLayered();
        }

        private void DetectHoliday(QuoteBatch batch)
        {
            DateTime now = MarketClock.Now;
            if (_session == null || !_session.CanShow) return;
            if (now.TimeOfDay < new TimeSpan(9, 20, 0)) return;
            if (batch.Quotes.Count == 0) return;
            if (batch.HasTodayData)
            {
                _holidayDetected = false;
                return;
            }
            if (batch.LatestQuoteTime.Date < now.Date)
                _holidayDetected = true;
        }

        private void UpdateTray()
        {
            if (_tray == null) return;
            SessionInfo s = EffectiveSession();
            SessionKind kind = s != null ? s.Kind : SessionKind.Closed;
            if (kind != _trayKind)
            {
                _tray.Icon = TrayIconFactory.Get(kind);
                _trayKind = kind;
            }

            string status = s != null ? s.Label : "休市";
            string tip = "A股行情条 · " + status;
            bool mayShowPrice = (s != null && s.CanShow) || (_manualOverride && _manualVisible);
            if (mayShowPrice && _displayQuotes.Count > 0)
            {
                StringBuilder parts = new StringBuilder(status);
                foreach (Quote q in DisplayedQuotes())
                {
                    if (q == null || q.Price <= 0) continue;
                    parts.Append(" · ").Append(q.Name).Append(' ')
                         .Append(q.PriceText).Append(' ').Append(q.ChangeText);
                }
                if (parts.Length > status.Length) tip = parts.ToString();
            }
            else if (!string.IsNullOrEmpty(_lastError))
            {
                tip = status + " · 网络异常";
            }
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            if (_tray.Text != tip) _tray.Text = tip;
        }

        private void ToggleVisible()
        {
            _manualOverride = true;
            _manualVisible = !Visible;
            UpdateVisibility(MarketClock.Now);
            if (_manualVisible) BeginFetch(true);
        }

        private void ToggleAutoStart()
        {
            bool next = !StartupManager.IsEnabled();
            bool ok = StartupManager.Set(next);
            if (!ok)
            {
                MessageBox.Show(this, "无法修改开机启动项，请检查系统权限。", "开机启动",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _config.AutoStart = next;
            try { _config.Save(); } catch { }
        }

        private void ToggleMono()
        {
            _config.MonoColor = !_config.MonoColor;
            try { _config.Save(); } catch { }
            if (Visible) RenderLayered();
        }

        private void TogglePrivacy()
        {
            _config.PrivacyMode = !_config.PrivacyMode;
            try { _config.Save(); } catch { }
            if (!Visible) return;
            if (_config.PrivacyMode) StartFade(_hover || _menuOpen);
            else { StopFade(); RenderLayered(); }
        }

        private void SetFrostLevel(int level)
        {
            _config.FrostLevel = level;
            try { _config.Save(); } catch { }
            if (Visible && !_fading) RenderLayered();
        }

        private void OpenSettings()
        {
            AppConfig draft = _config.Clone();
            using (SettingsForm form = new SettingsForm(draft))
            {
                if (!Visible) form.TopMost = true;
                DialogResult result = Visible ? form.ShowDialog(this) : form.ShowDialog();
                if (result != DialogResult.OK) return;
            }

            _config.Stocks = new List<StockItem>();
            foreach (StockItem s in draft.Stocks) _config.Stocks.Add(s.Clone());
            _config.RefreshSeconds = draft.RefreshSeconds;
            _config.CarouselSeconds = draft.CarouselSeconds;
            _config.FontSize = draft.FontSize;
            _config.HideWhenClosed = draft.HideWhenClosed;
            _config.AutoStart = draft.AutoStart;
            _config.ConfigPath = draft.ConfigPath;

            _dpi = DetectDpi();
            _startIndex = 0;
            BuildDisplayQuotes();
            UpdatePlacement(true);
            UpdateVisibility(MarketClock.Now);
            if (Visible) RenderLayered();
            BeginFetch(true);
        }

        private void OpenConfigFile()
        {
            try { Process.Start("notepad.exe", "\"" + _config.ConfigPath + "\""); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开配置文件：" + ex.Message, "打开配置文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowAbout()
        {
            string ver = GetType().Assembly.GetName().Version.ToString(3);
            string text = "A股行情条 v" + ver + "（任务栏透明版）\r\n\r\n" +
                "· 每行显示名称、价格、涨跌幅，行末附今日分时走势缩略图\r\n" +
                "· 红涨绿跌，价格颜色即涨跌方向\r\n" +
                "· 每 " + _config.CarouselSeconds + " 秒轮播下一组，悬停暂停（隐私模式下悬停不停）\r\n" +
                "· 午休、收盘后、周末与节假日自动隐藏\r\n" +
                "· 右键菜单可设置、刷新、退出\r\n\r\n" +
                "行情来源：腾讯财经 qt.gtimg.cn\r\n" +
                "仅为看盘辅助，不构成投资建议。";
            MessageBox.Show(this, text, "关于 A股行情条",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExitApp()
        {
            _exiting = true;
            _timer.Stop();
            if (_hoverTimer != null) _hoverTimer.Stop();
            if (_fadeTimer != null) _fadeTimer.Stop();
            _tray.Visible = false;
            Close();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _manualOverride = true;
                _manualVisible = false;
                Hide();
                return;
            }
            _timer.Stop();
            if (_hoverTimer != null) _hoverTimer.Stop();
            if (_fadeTimer != null) _fadeTimer.Stop();
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                }
                catch { }
                if (_timer != null) _timer.Dispose();
                if (_hoverTimer != null) _hoverTimer.Dispose();
                if (_fadeTimer != null) _fadeTimer.Dispose();
                StopFade();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                if (_menu != null) _menu.Dispose();
                TrayIconFactory.DisposeAll();
            }
            base.Dispose(disposing);
        }
    }
}
