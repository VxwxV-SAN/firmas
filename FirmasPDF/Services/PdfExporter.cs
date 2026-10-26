using System.IO;
using System.Windows;
using FirmasPDF.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace FirmasPDF.Services;

public sealed class PdfExporter
{
    public Task ExportAsync(PdfSession source, IReadOnlyList<PlacementState> placements,
        string destination, SigningRequest? signing = null, Guid? selectedPlacementId = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // PDFsharp's WPF image path stays on its own STA thread; the main window remains responsive.
        var thread = new Thread(() =>
        {
            try { Export(source, placements, destination, signing, selectedPlacementId); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "PDF export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Export(PdfSession source, IReadOnlyList<PlacementState> placements,
        string destination, SigningRequest? signing, Guid? selectedPlacementId)
    {
        if (!source.CanExport) throw new InvalidOperationException(source.RestrictionMessage);
        var fullDestination = Path.GetFullPath(destination);
        if (string.Equals(fullDestination, source.SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elige un nombre diferente al PDF abierto para guardar la copia.");
        using var input = new MemoryStream(source.OriginalBytes, false);
        using var document = PdfReader.Open(input, source.Password, PdfDocumentOpenMode.Modify);
        if (PdfSession.ContainsDigitalSignature(document))
            throw new InvalidOperationException("El documento ya contiene una firma digital y está disponible para lectura.");

        PlacementState? appearance = null;
        if (signing != null)
        {
            CertificateService.EnsureUsable(signing.Certificate);
            if (signing.UseSelectedAppearance)
                appearance = placements.FirstOrDefault(p => p.Id == selectedPlacementId)
                    ?? throw new InvalidOperationException("Selecciona una firma sobre el documento para usarla como apariencia digital.");
        }
        foreach (var placement in placements)
        {
            var b = placement.Bounds;
            if (placement.PageIndex < 0 || placement.PageIndex >= document.PageCount ||
                !double.IsFinite(b.X + b.Y + b.Width + b.Height) || b.Width <= 0 || b.Height <= 0 ||
                b.X < 0 || b.Y < 0 || b.Right > 1.000001 || b.Bottom > 1.000001)
                throw new InvalidOperationException("Una firma está fuera del área visible de la página.");
        }

        foreach (var group in placements.Where(p => p.Id != appearance?.Id).GroupBy(p => p.PageIndex))
        {
            var page = document.Pages[group.Key];
            var geometry = PdfGeometry.FromPage(page);
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            graphics.MultiplyTransform(geometry.DisplayToGraphicsMatrix);
            foreach (var placement in group)
            {
                using var png = new MemoryStream(placement.Asset.PngBytes, false);
                using var image = XImage.FromStream(png);
                graphics.DrawImage(image, geometry.ToDisplayRect(placement.Bounds));
            }
        }

        if (signing != null)
        {
            var pageIndex = appearance?.PageIndex ?? 0;
            var geometry = PdfGeometry.FromPage(document.Pages[pageIndex]);
            DigitalSignatureHandler.ForDocument(document,
                new PdfSharpDefaultSigner(signing.Certificate, PdfMessageDigestType.SHA256),
                new DigitalSignatureOptions
                {
                    AppName = "FirmasPDF",
                    Reason = signing.Reason,
                    Location = signing.Location,
                    PageIndex = pageIndex,
                    Rectangle = appearance == null ? new XRect(0, 0, 0, 0) : geometry.ToPdfRect(appearance.Bounds),
                    AppearanceHandler = appearance == null ? null :
                        new ImageSignatureAppearance(appearance.Asset.PngBytes, geometry.Rotation)
                });
        }

        var temp = fullDestination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                document.Save(output, false);
            File.Move(temp, fullDestination, true);
        }
        finally { File.Delete(temp); }
    }

    private sealed class ImageSignatureAppearance(byte[] pngBytes, int rotation) : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics graphics, XRect rectangle)
        {
            var w = rectangle.Width;
            var h = rectangle.Height;
            var matrix = rotation switch
            {
                90 => new XMatrix(0, -1, 1, 0, 0, h),
                180 => new XMatrix(-1, 0, 0, -1, w, h),
                270 => new XMatrix(0, 1, -1, 0, w, 0),
                _ => XMatrix.Identity
            };
            graphics.MultiplyTransform(matrix);
            using var png = new MemoryStream(pngBytes, false);
            using var image = XImage.FromStream(png);
            graphics.DrawImage(image, 0, 0, rotation is 90 or 270 ? h : w,
                rotation is 90 or 270 ? w : h);
        }
    }
}
