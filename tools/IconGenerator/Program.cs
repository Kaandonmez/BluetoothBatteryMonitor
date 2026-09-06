using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGenerator;

class Program
{
    static void Main()
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        
        string assetsDir = Path.Combine(rootDir, "src", "BluetoothBatteryMonitor.App", "Assets");
        Directory.CreateDirectory(assetsDir);
        string icoPath = Path.Combine(assetsDir, "app.ico");

        string docsDir = Path.Combine(rootDir, "docs");
        Directory.CreateDirectory(docsDir);
        string previewPath = Path.Combine(docsDir, "app_icon_preview.png");

        Console.WriteLine($"Generating multi-resolution icon to: {icoPath}");
        SaveMultiResIcon(sizes, icoPath);

        using (var previewBmp = CreateAppIcon(256))
        {
            previewBmp.Save(previewPath, ImageFormat.Png);
            Console.WriteLine($"Saved high-res preview to: {previewPath}");
        }

        Console.WriteLine("Icon generation completed successfully!");
    }

    public static Bitmap CreateAppIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = size / 256f;

        // 1. Fluent Background Squircle
        float margin = 10f * s;
        float w = size - (2 * margin);
        float h = size - (2 * margin);
        float radius = 48f * s;

        using (var path = CreateRoundedRectPath(margin, margin, w, h, radius))
        {
            using (var bgBrush = new LinearGradientBrush(
                new PointF(margin, margin),
                new PointF(margin + w, margin + h),
                Color.FromArgb(255, 13, 23, 50),
                Color.FromArgb(255, 20, 52, 102)))
            {
                g.FillPath(bgBrush, path);
            }

            using var rimPen = new Pen(Color.FromArgb(180, 56, 189, 248), Math.Max(1.0f, 2.5f * s));
            g.DrawPath(rimPen, path);
        }

        // 2. Bluetooth Symbol
        float btX = 94f * s;
        float btY = 128f * s;
        float btH = 114f * s;
        float topY = btY - (btH / 2f);
        float botY = btY + (btH / 2f);
        float wingW = 32f * s;
        float wingH = btH * 0.26f;

        using (var btPen = new Pen(Color.FromArgb(255, 241, 245, 249), Math.Max(1.5f, 9.5f * s)))
        {
            btPen.StartCap = LineCap.Round;
            btPen.EndCap = LineCap.Round;
            btPen.LineJoin = LineJoin.Round;

            // Center vertical spine
            g.DrawLine(btPen, btX, topY, btX, botY);

            // Diagonal 1: Lower-left to Upper-right
            var pLeftBot = new PointF(btX - wingW, btY + wingH);
            var pRightTop = new PointF(btX + wingW, topY + wingH);
            g.DrawLine(btPen, pLeftBot, pRightTop);

            // Top loop back to top spine
            var pTop = new PointF(btX, topY);
            g.DrawLine(btPen, pRightTop, pTop);

            // Diagonal 2: Upper-left to Lower-right
            var pLeftTop = new PointF(btX - wingW, btY - wingH);
            var pRightBot = new PointF(btX + wingW, botY - wingH);
            g.DrawLine(btPen, pLeftTop, pRightBot);

            // Bottom loop back to bottom spine
            var pBot = new PointF(btX, botY);
            g.DrawLine(btPen, pRightBot, pBot);
        }

        // 3. Battery Telemetry Pillar
        float batX = 158f * s;
        float batY = 74f * s;
        float batW = 56f * s;
        float batH = 104f * s;
        float batRad = 10f * s;

        using (var batPath = CreateRoundedRectPath(batX, batY, batW, batH, batRad))
        {
            using (var batShellBrush = new SolidBrush(Color.FromArgb(235, 15, 23, 42)))
            {
                g.FillPath(batShellBrush, batPath);
            }

            using (var batBorderPen = new Pen(Color.FromArgb(255, 100, 116, 139), Math.Max(1.0f, 3.5f * s)))
            {
                g.DrawPath(batBorderPen, batPath);
            }
        }

        float termW = 20f * s;
        float termH = 7f * s;
        float termX = batX + ((batW - termW) / 2f);
        float termY = batY - termH + (1.2f * s);
        using (var termBrush = new SolidBrush(Color.FromArgb(255, 148, 163, 184)))
        {
            g.FillRectangle(termBrush, termX, termY, termW, termH);
        }

        float fillPad = 4.5f * s;
        float maxFillH = batH - (2 * fillPad);
        float fillH = maxFillH * 0.80f;
        float fillY = batY + batH - fillPad - fillH;
        float fillW = batW - (2 * fillPad);
        float fillX = batX + fillPad;

        if (fillH > 0 && fillW > 0)
        {
            using (var fillBrush = new LinearGradientBrush(
                new PointF(fillX, fillY),
                new PointF(fillX, fillY + fillH),
                Color.FromArgb(255, 52, 211, 153),
                Color.FromArgb(255, 16, 185, 129)))
            {
                g.FillRectangle(fillBrush, fillX, fillY, fillW, fillH);
            }
        }

        // 4. White Energy Charging Bolt
        if (size >= 32)
        {
            float bx = batX + (batW / 2f);
            float by = batY + (batH / 2f);
            float bw = 15f * s;
            float bh = 34f * s;

            PointF[] boltPts =
            [
                new PointF(bx + (bw * 0.12f), by - (bh * 0.50f)),
                new PointF(bx - (bw * 0.60f), by + (bh * 0.06f)),
                new PointF(bx - (bw * 0.05f), by + (bh * 0.06f)),
                new PointF(bx - (bw * 0.22f), by + (bh * 0.50f)),
                new PointF(bx + (bw * 0.60f), by - (bh * 0.06f)),
                new PointF(bx + (bw * 0.05f), by - (bh * 0.06f))
            ];

            using var boltPath = new GraphicsPath();
            boltPath.AddLines(boltPts);
            boltPath.CloseFigure();

            using var boltBrush = new SolidBrush(Color.White);
            g.FillPath(boltBrush, boltPath);
        }

        return bmp;
    }

    private static GraphicsPath CreateRoundedRectPath(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SaveMultiResIcon(int[] sizes, string outputPath)
    {
        var images = new List<(int size, byte[] bytes)>();
        foreach (int sz in sizes)
        {
            using var bmp = CreateAppIcon(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            images.Add((sz, ms.ToArray()));
        }

        using var fs = new FileStream(outputPath, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)images.Count);

        int offset = 6 + (images.Count * 16);

        foreach (var (sz, bytes) in images)
        {
            byte w = sz >= 256 ? (byte)0 : (byte)sz;
            byte h = sz >= 256 ? (byte)0 : (byte)sz;

            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)bytes.Length);
            bw.Write((uint)offset);

            offset += bytes.Length;
        }

        foreach (var (_, bytes) in images)
        {
            bw.Write(bytes);
        }
    }
}