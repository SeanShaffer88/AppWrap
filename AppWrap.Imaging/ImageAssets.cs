using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppWrap.Imaging;

public static class ImageAssets
{
    public static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("Choose an image smaller than 32 MB.");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count > 64) throw new IOException("Choose a still image or an icon with at most 64 frames.");
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 32_000_000)
            throw new IOException("Choose a picture up to 32 megapixels and 8192 pixels per side.");
        // Read dimensions before pixel decoding, then copy just the selected bounded frame.
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)];
        converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        var result = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, frame.PixelWidth * 4);
        result.Freeze(); return result;
    }

    public static BitmapSource DefaultIcon()
    {
        var pixels = new byte[128 * 128 * 4];
        for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
        {
            var i = (y * 128 + x) * 4;
            bool white = x is > 29 and < 98 && y is > 28 and < 99 && (x < 38 || x > 89 || y < 37 || y > 90 || y is > 59 and < 68);
            pixels[i] = white ? (byte)244 : (byte)42; pixels[i + 1] = white ? (byte)244 : (byte)23; pixels[i + 2] = white ? (byte)244 : (byte)118; pixels[i + 3] = 255;
        }
        var result = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, pixels, 128 * 4); result.Freeze(); return result;
    }

    private static BitmapSource Fit(BitmapSource source, int width, int height, bool crop = false)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (crop) context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var scale = crop ? Math.Max((double)width / source.PixelWidth, (double)height / source.PixelHeight) : Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
            var w = source.PixelWidth * scale; var h = source.PixelHeight * scale;
            context.DrawImage(source, new Rect((width - w) / 2, (height - h) / 2, w, h));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    public static void WriteIcons(string? sourcePath, string directory)
    {
        Directory.CreateDirectory(directory);
        var source = sourcePath == null ? DefaultIcon() : Load(sourcePath);
        foreach (int size in new[] { 16, 32, 75, 128, 256 })
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Fit(source, size, size)));
            using var file = File.Create(Path.Combine(directory, $"appicon_{size}.png")); encoder.Save(file);
        }
        var frames = new List<(int Size, int Bits, byte[] Data)>();
        foreach (int size in new[] { 16, 32, 48 }) foreach (int bits in new[] { 8, 32 }) frames.Add((size, bits, Dib(Fit(source, size, size), bits)));
        var largeEncoder = new PngBitmapEncoder(); largeEncoder.Frames.Add(BitmapFrame.Create(Fit(source, 256, 256)));
        using (var large = new MemoryStream()) { largeEncoder.Save(large); frames.Add((256, 32, large.ToArray())); }
        using var icon = new BinaryWriter(File.Create(Path.Combine(directory, "appicon.ico")));
        icon.Write((ushort)0); icon.Write((ushort)1); icon.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var frame in frames)
        {
            icon.Write((byte)frame.Size); icon.Write((byte)frame.Size); icon.Write((byte)0); icon.Write((byte)0); icon.Write((ushort)1); icon.Write((ushort)frame.Bits); icon.Write(frame.Data.Length); icon.Write(offset); offset += frame.Data.Length;
        }
        foreach (var frame in frames) icon.Write(frame.Data);
    }

    private static byte[] Dib(BitmapSource source, int bits)
    {
        var size = source.PixelWidth; var pixelStride = ((size * bits + 31) / 32) * 4; var maskStride = ((size + 31) / 32) * 4;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var pixels = new byte[size * size * 4]; converted.CopyPixels(pixels, size * 4, 0);
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((ushort)1); writer.Write((ushort)bits); writer.Write(0); writer.Write(pixelStride * size + maskStride * size); writer.Write(0); writer.Write(0); writer.Write(bits == 8 ? 256 : 0); writer.Write(0);
        if (bits == 8) for (int i = 0; i < 256; i++) { writer.Write((byte)((i & 3) * 255 / 3)); writer.Write((byte)(((i >> 2) & 7) * 255 / 7)); writer.Write((byte)(((i >> 5) & 7) * 255 / 7)); writer.Write((byte)0); }
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                var index = (y * size + x) * 4;
                if (bits == 32) writer.Write(pixels.AsSpan(index, 4));
                else writer.Write((byte)((pixels[index + 2] >> 5 << 5) | (pixels[index + 1] >> 5 << 2) | (pixels[index] >> 6)));
            }
            for (int pad = size * bits / 8; pad < pixelStride; pad++) writer.Write((byte)0);
        }
        for (int y = size - 1; y >= 0; y--)
        {
            var mask = new byte[maskStride];
            for (int x = 0; x < size; x++) if (pixels[(y * size + x) * 4 + 3] < 128) mask[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(mask);
        }
        return memory.ToArray();
    }

    public static void WriteSidebar(string sourcePath, string outputPath)
    {
        var source = Fit(Load(sourcePath), 164, 314, crop: true);
        var frame = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame)); using var file = File.Create(outputPath); encoder.Save(file);
    }
}
