using System.Windows;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FirmasPDF.Services;

public sealed record PdfGeometry(double CropX, double CropY, double CropWidth,
    double CropHeight, double MediaHeight, int Rotation)
{
    public double DisplayWidth => Rotation is 90 or 270 ? CropHeight : CropWidth;
    public double DisplayHeight => Rotation is 90 or 270 ? CropWidth : CropHeight;

    public static PdfGeometry FromPage(PdfPage page)
    {
        var media = page.MediaBox;
        var crop = page.HasCropBox ? page.CropBox : media;
        var x1 = Math.Max(media.X1, crop.X1);
        var y1 = Math.Max(media.Y1, crop.Y1);
        var x2 = Math.Min(media.X2, crop.X2);
        var y2 = Math.Min(media.Y2, crop.Y2);
        if (x2 <= x1 || y2 <= y1)
            throw new InvalidOperationException("La página tiene un área visible inválida.");
        var rotation = ((page.Rotate % 360) + 360) % 360;
        if (rotation % 90 != 0)
            throw new InvalidOperationException("El PDF tiene una rotación de página no compatible.");
        return new PdfGeometry(x1, y1, x2 - x1, y2 - y1, media.Height, rotation);
    }

    public XRect ToDisplayRect(Rect bounds) => new(bounds.X * DisplayWidth,
        bounds.Y * DisplayHeight, bounds.Width * DisplayWidth, bounds.Height * DisplayHeight);

    // XGraphics uses top-left (0, MediaHeight); PDF coordinates use the absolute bottom-left.
    // Compensate for /Rotate, /CropBox, and even a nonzero /MediaBox origin.
    public XMatrix DisplayToGraphicsMatrix => Rotation switch
    {
        90 => new(0, -1, 1, 0, CropX, MediaHeight - CropY),
        180 => new(-1, 0, 0, -1, CropX + CropWidth, MediaHeight - CropY),
        270 => new(0, 1, -1, 0, CropX + CropWidth, MediaHeight - CropY - CropHeight),
        _ => new(1, 0, 0, 1, CropX, MediaHeight - CropY - CropHeight)
    };

    public XPoint DisplayToPdf(double x, double y) => Rotation switch
    {
        90 => new(CropX + y, CropY + x),
        180 => new(CropX + CropWidth - x, CropY + y),
        270 => new(CropX + CropWidth - y, CropY + CropHeight - x),
        _ => new(CropX + x, CropY + CropHeight - y)
    };

    public XRect ToPdfRect(Rect bounds)
    {
        var display = ToDisplayRect(bounds);
        var p1 = DisplayToPdf(display.X, display.Y);
        var p2 = DisplayToPdf(display.Right, display.Bottom);
        return new XRect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
            Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y));
    }
}
