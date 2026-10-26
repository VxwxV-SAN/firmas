using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FirmasPDF.Services;

public static class SignatureImageService
{
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        if ((long)image.PixelWidth * image.PixelHeight > 24_000_000)
            throw new InvalidOperationException("La imagen es demasiado grande. Usa una imagen de hasta 24 megapíxeles.");
        image.Freeze();
        return image;
    }

    public static BitmapSource FromStrokes(StrokeCollection strokes)
    {
        if (strokes.Count == 0)
            throw new InvalidOperationException("Dibuja tu firma antes de guardarla.");
        var bounds = strokes.GetBounds();
        bounds.Inflate(8, 8);
        const double scale = 2;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            strokes.Draw(context);
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource PrepareImport(BitmapSource source, bool removeWhiteBackground)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            if (removeWhiteBackground)
            {
                // Remove neutral paper whites while preserving colored ink and antialiased edges.
                var min = Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2]));
                if (min > 210)
                    pixels[offset + 3] = (byte)(pixels[offset + 3] * (255 - min) / 45);
            }
            if (pixels[offset + 3] <= 8) continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        if (maxX < minX)
            throw new InvalidOperationException("La imagen no contiene una firma visible. Prueba a conservar el fondo blanco.");
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        minX = Math.Max(0, minX - 8);
        minY = Math.Max(0, minY - 8);
        maxX = Math.Min(width - 1, maxX + 8);
        maxY = Math.Min(height - 1, maxY + 8);
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
        cropped.Freeze();
        return cropped;
    }

    public static void SavePng(BitmapSource image, Stream destination)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(destination);
    }
}
