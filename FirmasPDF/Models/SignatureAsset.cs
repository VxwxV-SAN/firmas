using System.IO;
using System.Windows.Media.Imaging;
using FirmasPDF.Infrastructure;

namespace FirmasPDF.Models;

public enum SignatureKind { Drawn, Imported }

public sealed class SignatureAsset(Guid id, string name, SignatureKind kind,
    DateTimeOffset createdAt, BitmapSource image) : ObservableObject
{
    private string _name = name;
    public Guid Id { get; } = id;
    public string Name { get => _name; internal set => Set(ref _name, value); }
    public SignatureKind Kind { get; } = kind;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public BitmapSource Image { get; } = image;
    // Encoded on the UI thread: a cached BitmapFrame can retain a thread-affine decoder even
    // when frozen. Export consumes only PNG bytes, never WPF objects from another thread.
    internal byte[] PngBytes { get; } = Encode(image);
    public string KindLabel => Kind == SignatureKind.Drawn ? "Dibujada" : "Importada";
    public double AspectRatio => (double)Image.PixelWidth / Image.PixelHeight;

    private static byte[] Encode(BitmapSource image)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return stream.ToArray();
    }
}
