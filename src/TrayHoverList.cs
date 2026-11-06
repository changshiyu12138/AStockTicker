using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AStockTicker
{
    /// <summary>One row of the tray hover list.</summary>
    public class HoverRow
    {
        public string Tag = "";      // "hk" / "us" / "" (A-share needs no tag)
        public string Name = "";
        public string Price = "--";
        public string Pct = "--";
        public string Note = "";     // "盘前" / "盘后"
        public Color Tint = Color.Gray;
        public bool Dim;             // market closed → muted row, sorted below
    }

    /// <summary>Borderless layered flyout listing the whole watch list while the cursor
    /// rests on the tray icon: in-session markets first, closed ones dimmed below.</summary>
    public class TrayHoverList : Form
    {
        private List<HoverRow> _rows = new List<HoverRow>();
        private bool _light = true;
        private Bitmap _frame;
        private Timer _fade;
        private int _alpha;
        private int _target;
        private Point _loc;
        private bool _placed;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage,
            out IntPtr bits, IntPtr hSection, uint offset);
        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION blend, uint flags);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private struct POINT { public int X; public int Y; }
        private struct SIZE { public int CX; public int CY; }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        private const uint ULW_ALPHA = 0x00000002;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOOWNERZORDER = 0x0200;

        public TrayHoverList()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x00000008 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>Renders the row list into the layered buffer and sizes the window.</summary>
        public void SetRows(List<HoverRow> rows, bool light)
        {
            _rows = rows ?? new List<HoverRow>();
            _light = light;
            RenderFrame();
        }

        /// <summary>Puts the flyout above the tray icon (right aligned) and fades in.</summary>
        public void ShowAbove(Rectangle anchor)
        {
            if (_frame == null || _rows.Count == 0) return;
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int x = anchor.Right - Width + 6;
            if (x < screen.Left + 8) x = screen.Left + 8;
            if (x + Width > screen.Right - 8) x = screen.Right - 8 - Width;
            int y = anchor.Top - Height - 6;
            if (!_placed || Location != new Point(x, y))
            {
                _loc = new Point(x, y);
                SetWindowPos(Handle, HWND_TOPMOST, x, y, Width, Height,
                    SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                _placed = true;
            }
            FadeTo(255);
        }

        public void FadeOut()
        {
            FadeTo(0);
        }

        /// <summary>True while any part of the fade animation is on screen.</summary>
        public bool Hovering
        {
            get { return _alpha > 0; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_fade == null)
            {
                _fade = new Timer();
                _fade.Interval = 30;
                _fade.Tick += delegate { FadeTick(); };
            }
        }

        private void FadeTo(int target)
        {
            if (_alpha == target) return;
            _target = target;
            if (_alpha == 0 && target == 255)
            {
                _alpha = 0;
                Present(_alpha);
                Show();
            }
            if (_fade != null) _fade.Start();
        }

        private void FadeTick()
        {
            int step = 64;
            if (_alpha < _target) _alpha = Math.Min(_target, _alpha + step);
            else if (_alpha > _target) _alpha = Math.Max(_target, _alpha - step);
            if (_alpha == 0)
            {
                _fade.Stop();
                Hide();
                return;
            }
            if (_alpha == _target) _fade.Stop();
            Present(_alpha);
        }

        private void RenderFrame()
        {
            Bitmap old = _frame;
            _frame = BuildFrame();
            if (old != null) old.Dispose();
            if (_alpha > 0) Present(_alpha);
        }

        private Bitmap BuildFrame()
        {
            int tagW = 24;
            int nameW = 0, priceW = 0, pctW = 0, noteW = 0;
            bool anyNote = false;
            int dividerAfter = -1;

            using (Graphics mg = Graphics.FromHwnd(IntPtr.Zero))
            {
                float s = Math.Max(0.75f, Math.Min(3f, mg.DpiX / 96f));
                float pad = 10f * s;
                float rowH = 25f * s;
                float dividerH = 9f * s;
                float tagF = Math.Max(1f, 24f * s);
                using (Font nameFont = new Font("Microsoft YaHei UI", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font priceFont = new Font("Microsoft YaHei UI", 12.5f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font smallFont = new Font("Microsoft YaHei UI", 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat sf = StringFormat.GenericTypographic)
                {
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        HoverRow r = _rows[i];
                        nameW = Math.Max(nameW, (int)Math.Ceiling(mg.MeasureString(r.Name, nameFont, PointF.Empty, sf).Width));
                        priceW = Math.Max(priceW, (int)Math.Ceiling(mg.MeasureString(r.Price, priceFont, PointF.Empty, sf).Width));
                        pctW = Math.Max(pctW, (int)Math.Ceiling(mg.MeasureString(r.Pct, nameFont, PointF.Empty, sf).Width));
                        if (!string.IsNullOrEmpty(r.Note))
                        {
                            anyNote = true;
                            noteW = Math.Max(noteW, (int)Math.Ceiling(mg.MeasureString(r.Note, smallFont, PointF.Empty, sf).Width));
                        }
                        if (r.Dim && dividerAfter < 0) dividerAfter = i;
                    }

                    tagW = (int)Math.Ceiling(tagF);
                    int w = (int)(pad * 2f) + tagW + (int)(6 * s) + nameW + (int)(12 * s) + priceW + (int)(10 * s) + pctW;
                    if (anyNote) w += (int)(10 * s) + noteW;
                    w += 6;
                    bool split = dividerAfter > 0 && dividerAfter < _rows.Count;
                    int h = (int)(pad * 2f + _rows.Count * rowH + (split ? dividerH : 0f));
                    if (w < 150) w = 150;

                    Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);

                        Color bg = _light ? Color.FromArgb(242, 250, 250, 252) : Color.FromArgb(242, 40, 41, 46);
                        Color border = _light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(34, 255, 255, 255);
                        Color nameCol = _light ? Color.FromArgb(32, 32, 36) : Color.FromArgb(238, 240, 244);
                        Color muted = _light ? Color.FromArgb(140, 144, 152) : Color.FromArgb(150, 156, 166);
                        Color dimName = _light ? Color.FromArgb(158, 162, 170) : Color.FromArgb(132, 134, 140);
                        Color divider = _light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255);

                        Rectangle body = new Rectangle(0, 0, w - 1, h - 1);
                        using (GraphicsPath path = RoundedRect(body, 9f * s))
                        using (SolidBrush bb = new SolidBrush(bg))
                        using (Pen bp = new Pen(border, 1f))
                        {
                            g.FillPath(bb, path);
                            g.DrawPath(bp, path);
                        }

                        float y = pad;
                        sf.LineAlignment = StringAlignment.Center;
                        for (int i = 0; i < _rows.Count; i++)
                        {
                            HoverRow r = _rows[i];
                            if (i == dividerAfter && split)
                            {
                                using (Pen dp = new Pen(divider, 1f))
                                    g.DrawLine(dp, pad + 2f, y + dividerH / 2f, w - pad - 2f, y + dividerH / 2f);
                                y += dividerH;
                            }

                            Color nCol = r.Dim ? dimName : nameCol;
                            Color pCol = r.Dim ? dimName : r.Tint;
                            Color tagCol = muted;
                            RectangleF rowRect = new RectangleF(0f, y, w, rowH);

                            sf.Alignment = StringAlignment.Near;
                            if (r.Tag.Length > 0)
                                g.DrawString(r.Tag, smallFont, new SolidBrush(tagCol),
                                    new RectangleF(pad, rowRect.Y, tagW, rowH), sf);
                            float x = pad + tagW + 6f * s;
                            g.DrawString(r.Name, nameFont, new SolidBrush(nCol),
                                new RectangleF(x, rowRect.Y, nameW + 2f, rowH), sf);
                            x += nameW + 12f * s;
                            g.DrawString(r.Price, priceFont, new SolidBrush(pCol),
                                new RectangleF(x, rowRect.Y, priceW + 2f, rowH), sf);
                            x += priceW + 10f * s;
                            g.DrawString(r.Pct, nameFont, new SolidBrush(pCol),
                                new RectangleF(x, rowRect.Y, pctW + 2f, rowH), sf);
                            if (anyNote)
                            {
                                x += pctW + 10f * s;
                                if (r.Note.Length > 0)
                                    g.DrawString(r.Note, smallFont, new SolidBrush(tagCol),
                                        new RectangleF(x, rowRect.Y, noteW + 2f, rowH), sf);
                            }
                            y += rowH;
                        }
                    }
                    return bmp;
                }
            }
        }

        /// <summary>Presents the cached frame with a global constant alpha.</summary>
        private void Present(int alpha)
        {
            if (_frame == null || !IsHandleCreated) return;
            int w = _frame.Width;
            int h = _frame.Height;

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
                    g.DrawImage(_frame, 0, 0, w, h);
                }

                POINT dst = new POINT();
                dst.X = _loc.X;
                dst.Y = _loc.Y;
                SIZE size = new SIZE();
                size.CX = w;
                size.CY = h;
                POINT src = new POINT();
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = (byte)alpha;
                blend.AlphaFormat = 1;
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
            }
            catch { }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fade != null) { _fade.Stop(); _fade.Dispose(); _fade = null; }
                if (_frame != null) { _frame.Dispose(); _frame = null; }
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
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
    }
}
