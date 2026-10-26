using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace FirmasPDF.Services;

public sealed record PdfPageInfo(double WidthDip, double HeightDip);

public sealed class PdfSession : IDisposable
{
    private readonly WinPdfDocument _document;
    private readonly MemoryStream _memory;
    private readonly IRandomAccessStream _input;
    public string SourcePath { get; }
    public byte[] OriginalBytes { get; }
    public string Password { get; }
    public int PageCount => Pages.Count;
    public IReadOnlyList<PdfPageInfo> Pages { get; }
    public bool CanExport { get; }
    public string RestrictionMessage { get; }

    private PdfSession(string path, byte[] bytes, string password, WinPdfDocument document,
        MemoryStream memory, IRandomAccessStream input)
    {
        SourcePath = path;
        OriginalBytes = bytes;
        Password = password;
        _document = document;
        _memory = memory;
        _input = input;
        var pages = new List<PdfPageInfo>();
        for (uint index = 0; index < document.PageCount; index++)
        {
            using var page = document.GetPage(index);
            pages.Add(new PdfPageInfo(page.Size.Width, page.Size.Height));
        }
        if (pages.Count == 0) throw new InvalidOperationException("El PDF no contiene páginas.");
        Pages = pages;
        (CanExport, RestrictionMessage) = Inspect(bytes, password);
    }

    public static async Task<PdfSession> OpenAsync(string path, string password = "")
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath);
        var memory = new MemoryStream(bytes, false);
        var input = memory.AsRandomAccessStream();
        try
        {
            var document = await WinPdfDocument.LoadFromStreamAsync(input, password);
            return new PdfSession(fullPath, bytes, password, document, memory, input);
        }
        catch { input.Dispose(); memory.Dispose(); throw; }
    }

    public async Task<BitmapSource> RenderAsync(int pageIndex)
    {
        using var page = _document.GetPage((uint)pageIndex);
        using var output = new InMemoryRandomAccessStream();
        var scale = Math.Min(2.5, 3600 / Math.Max(page.Size.Width, page.Size.Height));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale))
        };
        await page.RenderToStreamAsync(output, options);
        output.Seek(0);
        using var readable = output.AsStreamForRead();
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = readable;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static (bool CanExport, string Message) Inspect(byte[] bytes, string password)
    {
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var pdf = PdfReader.Open(stream, password, PdfDocumentOpenMode.Modify);
            if (ContainsDigitalSignature(pdf))
                return (false, "PDF firmado digitalmente · disponible para lectura");
            foreach (var page in pdf.Pages) _ = PdfGeometry.FromPage(page);
            return (true, "");
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("owner", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "PDF protegido · abre con la contraseña del propietario para exportar");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotSupportedException)
        {
            return (false, "Este PDF está disponible para lectura; su estructura no permite exportarlo con este motor.");
        }
    }

    public static bool ContainsDigitalSignature(PdfSharp.Pdf.PdfDocument document) =>
        document.Internals.GetAllObjects().OfType<PdfDictionary>().Any(dictionary =>
            (dictionary.Elements.GetName("/Type") == "/Sig" && dictionary.Elements.ContainsKey("/ByteRange")) ||
            (dictionary.Elements.GetName("/FT") == "/Sig" &&
             dictionary.Elements.GetDictionary("/V")?.Elements.ContainsKey("/ByteRange") == true));

    public void Dispose()
    {
        _input.Dispose();
        _memory.Dispose();
    }
}
