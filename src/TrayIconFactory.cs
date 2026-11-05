using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AStockTicker
{
    /// <summary>Creates small status-coloured tray icons at runtime.</summary>
    public static class TrayIconFactory
    {
        private static readonly Dictionary<int, Icon> Cache = new Dictionary<int, Icon>();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Get(SessionKind kind)
        {
            Color dot = Theme.ForSession(kind);
            int key = dot.ToArgb();
            lock (Cache)
            {
                if (Cache.ContainsKey(key)) return Cache[key];
                Icon icon = Build(dot);
                Cache[key] = icon;
                return icon;
            }
        }

        private static Icon Build(Color dot)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using (GraphicsPath path = RoundedRect(new RectangleF(1f, 1f, 30f, 30f), 7f))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(16, 24, 35)))
                using (Pen border = new Pen(Color.FromArgb(55, 76, 103), 1.2f))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                using (Font font = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("A", font, Brushes.White, new RectangleF(0f, -1f, 32f, 31f), sf);
                    sf.Dispose();
                }

                using (SolidBrush dotBrush = new SolidBrush(dot))
                    g.FillEllipse(dotBrush, 20f, 20f, 10f, 10f);
                using (Pen dotBorder = new Pen(Color.FromArgb(16, 24, 35), 1.5f))
                    g.DrawEllipse(dotBorder, 20f, 20f, 10f, 10f);

                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(h))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DisposeAll()
        {
            lock (Cache)
            {
                foreach (Icon icon in Cache.Values) icon.Dispose();
                Cache.Clear();
            }
        }
    }
}


namespace AStockTicker
{
    internal static class Theme
    {
        private static readonly Color Trading = Color.FromArgb(43, 217, 124);
        private static readonly Color Auction = Color.FromArgb(245, 166, 35);
        private static readonly Color Closed = Color.FromArgb(122, 133, 149);

        public static Color ForSession(SessionKind kind)
        {
            if (kind == SessionKind.Trading) return Trading;
            if (kind == SessionKind.PreMarket || kind == SessionKind.AfterHours) return Trading;
            if (kind == SessionKind.OpeningAuction || kind == SessionKind.PreOpen ||
                kind == SessionKind.ClosingAuction) return Auction;
            return Closed;
        }
    }
}
