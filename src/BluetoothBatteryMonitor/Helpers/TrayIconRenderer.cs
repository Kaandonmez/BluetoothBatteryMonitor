using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BluetoothBatteryMonitor.Helpers;

/// <summary>
/// Sistem tepsisi için dinamik pil ikonları çizen ve üreten yardımcı sınıf.
/// En düşük pil yüzdesine göre renk kodları (Yeşil: %40+, Sarı: %20-%39, Kırmızı: <%20) uygular.
/// </summary>
public static class TrayIconRenderer
{
    private static readonly System.Drawing.Color ColorGreen = System.Drawing.Color.FromArgb(34, 197, 94);   // #22C55E
    private static readonly System.Drawing.Color ColorYellow = System.Drawing.Color.FromArgb(234, 179, 8);   // #EAB308
    private static readonly System.Drawing.Color ColorRed = System.Drawing.Color.FromArgb(239, 68, 68);     // #EF4444
    private static readonly System.Drawing.Color ColorBluetooth = System.Drawing.Color.FromArgb(0, 120, 215); // #0078D7

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Pil yüzdesi ve şarj durumuna göre 32x32 dinamik System.Drawing.Icon oluşturur.
    /// </summary>
    public static Icon CreateTrayIcon(int? batteryLevel, bool isCharging = false)
    {
        using var bitmap = RenderBitmap(batteryLevel, isCharging, 32);
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// WPF H.NotifyIcon IconSource için BitmapSource üretir.
    /// </summary>
    public static BitmapSource CreateTrayBitmapSource(int? batteryLevel, bool isCharging = false)
    {
        using var icon = CreateTrayIcon(batteryLevel, isCharging);
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }

    /// <summary>
    /// Dinamik tepsisi görselini 32x32 veya 64x64 çöznürlükte çizer.
    /// </summary>
    public static Bitmap RenderBitmap(int? batteryLevel, bool isCharging, int size = 32)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(System.Drawing.Color.Transparent);

        if (!batteryLevel.HasValue)
        {
            DrawBluetoothSymbol(g, size);
            return bitmap;
        }

        int level = Math.Clamp(batteryLevel.Value, 0, 100);
        System.Drawing.Color statusColor = level switch
        {
            >= 40 => ColorGreen,
            >= 20 => ColorYellow,
            _ => ColorRed
        };

        // Modern pil gövdesi ve doluluk çubuğu
        // 1. Pil Dış Çerçevesi (Yuvarlatılmış dikdörtgen)
        float padding = size * 0.08f;
        float batWidth = size - (padding * 2);
        float batHeight = size * 0.44f;
        float batX = padding;
        float batY = size - batHeight - padding;

        using (var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(40, 255, 255, 255)))
        using (var borderPen = new Pen(System.Drawing.Color.FromArgb(200, 255, 255, 255), 1.5f))
        {
            // Pil gövdesi
            g.FillRectangle(bgBrush, batX, batY, batWidth - 3, batHeight);
            g.DrawRectangle(borderPen, batX, batY, batWidth - 3, batHeight);

            // Pil Kutbu (Nipple)
            float nippleWidth = 2.5f;
            float nippleHeight = batHeight * 0.45f;
            float nippleY = batY + (batHeight - nippleHeight) / 2f;
            g.FillRectangle(new SolidBrush(System.Drawing.Color.White), batX + batWidth - 3, nippleY, nippleWidth, nippleHeight);
        }

        // 2. Pil İçi Doluluk (Renk kodlu: Yeşil, Sarı, Kırmızı)
        float innerPadding = 2f;
        float maxFillWidth = (batWidth - 3) - (innerPadding * 2);
        float fillWidth = level > 0 ? Math.Max(2f, (maxFillWidth * level) / 100f) : 0f;
        float fillHeight = batHeight - (innerPadding * 2);

        if (fillWidth > 0)
        {
            using var fillBrush = new SolidBrush(statusColor);
            g.FillRectangle(fillBrush, batX + innerPadding, batY + innerPadding, fillWidth, fillHeight);
        }

        // 3. Yüzde Metni (Üst kısma şık, okunaklı yazı)
        string text = isCharging ? $"⚡{level}" : $"{level}";
        float fontSize = size >= 32 ? (level == 100 ? 9.0f : (isCharging ? 9.5f : 11.0f)) : 8f;
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(System.Drawing.Color.White);

        var textSize = g.MeasureString(text, font);
        float textX = Math.Max(0f, (size - textSize.Width) / 2f);
        float textY = Math.Max(0f, (batY - textSize.Height) / 2f);

        g.DrawString(text, font, textBrush, textX, textY);

        return bitmap;
    }

    private static void DrawBluetoothSymbol(Graphics g, int size)
    {
        // Bluetooth ikonu çizimi
        float cx = size / 2f;
        float cy = size / 2f;
        float h = size * 0.7f;
        float halfH = h / 2f;
        float w = size * 0.28f;

        using var pen = new Pen(ColorBluetooth, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        // Dikey orta hat
        g.DrawLine(pen, cx, cy - halfH, cx, cy + halfH);

        // Çapraz çizgiler
        g.DrawLine(pen, cx - w, cy - halfH * 0.5f, cx + w, cy + halfH * 0.5f);
        g.DrawLine(pen, cx + w, cy + halfH * 0.5f, cx, cy + halfH);
        g.DrawLine(pen, cx, cy - halfH, cx + w, cy - halfH * 0.5f);
        g.DrawLine(pen, cx + w, cy - halfH * 0.5f, cx - w, cy + halfH * 0.5f);
    }
}
