using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BluetoothBatteryMonitor.App.Helpers;

public static class TrayIconGenerator
{
    /// <summary>
    /// Verilen pil seviyesi ve şarj durumuna göre dinamik sistem tepsisi ikonu (Icon) üretir.
    /// </summary>
    /// <param name="batteryLevel">0-100 arası pil seviyesi, null ise bağlı cihaz yok</param>
    /// <param name="isCharging">Cihaz şarj oluyor mu</param>
    /// <param name="isDarkTheme">Kullanılan sistem teması koyu mu</param>
    public static Icon GenerateTrayIcon(int? batteryLevel, bool isCharging = false, bool isDarkTheme = true)
    {
        const int size = 32; // 32x32 yüksek DPI netliği sağlar
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Transparent);

            Color outlineColor = isDarkTheme ? Color.White : Color.FromArgb(40, 40, 40);
            Color fillColor;

            if (!batteryLevel.HasValue)
            {
                // Bağlı cihaz yok simgesi: Soluk gri pil gövdesi ve ortada çizgi
                Color inactiveColor = Color.FromArgb(120, outlineColor);
                using var inactivePen = new Pen(inactiveColor, 2f);
                g.DrawRoundedRectangle(inactivePen, new RectangleF(4, 8, 22, 16), 3);
                g.FillRectangle(new SolidBrush(inactiveColor), 26, 13, 2, 6);
                g.DrawLine(inactivePen, 8, 16, 22, 16);
            }
            else
            {
                int level = Math.Clamp(batteryLevel.Value, 0, 100);

                if (isCharging)
                {
                    fillColor = Color.FromArgb(0, 164, 239); // Şarj için parlak mavi / siyan
                }
                else if (level >= 40)
                {
                    fillColor = Color.FromArgb(16, 185, 129); // Modern Fluent Yeşil
                }
                else if (level >= 20)
                {
                    fillColor = Color.FromArgb(245, 158, 11); // Modern Kehribar/Turuncu
                }
                else
                {
                    fillColor = Color.FromArgb(239, 68, 68); // Modern Kırmızı
                }

                // 1. Pil Dış Gövdesi
                using var bodyPen = new Pen(outlineColor, 2f);
                var bodyRect = new RectangleF(2, 6, 24, 20);
                g.DrawRoundedRectangle(bodyPen, bodyRect, 4);

                // 2. Pil Kutbu (Terminal ucu)
                using var terminalBrush = new SolidBrush(outlineColor);
                g.FillRoundedRectangle(terminalBrush, new RectangleF(26, 12, 3, 8), 1);

                // 3. Pil İç Doluluğu (Yatay dolum)
                float maxInnerWidth = 20f;
                float innerWidth = Math.Max(2f, (maxInnerWidth * level) / 100f);
                var innerRect = new RectangleF(4, 8, innerWidth, 16);
                using var fillBrush = new SolidBrush(fillColor);
                g.FillRoundedRectangle(fillBrush, innerRect, 2);

                // 4. Şarj İkonu veya Yüzde Metni
                if (isCharging)
                {
                    // Küçük şimşek çizimi
                    PointF[] bolt =
                    [
                        new PointF(15, 8),
                        new PointF(11, 16),
                        new PointF(14, 16),
                        new PointF(12, 23),
                        new PointF(18, 14),
                        new PointF(15, 14)
                    ];
                    using var boltBrush = new SolidBrush(Color.White);
                    g.FillPolygon(boltBrush, bolt);
                }
                else if (level <= 99)
                {
                    // Rakamı pilin üzerine okunaklı ve kontrastlı çiz
                    string text = level.ToString();
                    using var font = new Font("Segoe UI", level == 100 ? 8f : 9f, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var textBrush = new SolidBrush(level >= 35 ? (isDarkTheme ? Color.Black : Color.White) : Color.White);
                    using var stringFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(text, font, textBrush, new RectangleF(2, 7, 24, 18), stringFormat);
                }
            }
        }

        IntPtr hIcon = bitmap.GetHicon();
        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private static void DrawRoundedRectangle(this Graphics g, Pen pen, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectPath(bounds, radius);
        g.DrawPath(pen, path);
    }

    private static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectPath(bounds, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));

        // Sol üst
        path.AddArc(arc, 180, 90);
        // Sağ üst
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        // Sağ alt
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // Sol alt
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
