using System.IO;
using System.Windows;
using System.Windows.Media;
using PdfEditor.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace PdfEditor.Services;

public sealed class PdfExporter
{
    public async Task ExportAsync(PdfSession source, IReadOnlyList<PlacementState> placements,
        string destination, SigningRequest? signing = null, Guid? selectedPlacementId = null) =>
        await ExportAsync(source, Array.Empty<EditObjectState>(), placements, destination, signing,
            selectedPlacementId);

    public Task<ExportResult> ExportAsync(PdfSession source, IReadOnlyList<EditObjectState> edits,
        IReadOnlyList<PlacementState> placements, string destination, SigningRequest? signing = null,
        Guid? selectedPlacementId = null)
    {
        var completion = new TaskCompletionSource<ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = Export(source, edits, placements, destination, signing, selectedPlacementId);
                completion.SetResult(result);
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "PDF export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ExportResult Export(PdfSession source, IReadOnlyList<EditObjectState> edits,
        IReadOnlyList<PlacementState> placements, string destination, SigningRequest? signing,
        Guid? selectedPlacementId)
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
            ValidateBounds(placement.PageIndex, placement.Bounds, document.PageCount, "firma");
        foreach (var edit in edits.Where(e => !e.IsDeleted || e.Kind is EditObjectKind.ExistingText or EditObjectKind.ExistingImage))
            ValidateBounds(edit.PageIndex, edit.Bounds, document.PageCount, "objeto de edición");

        var usedFallback = false;
        var ops = PdfEditEngine.Build(edits, preferNative: true);
        foreach (var group in ops.GroupBy(o => o.PageIndex))
        {
            var page = document.Pages[group.Key];
            var geometry = PdfGeometry.FromPage(page);
            foreach (var op in group)
            {
                try
                {
                    ApplyOp(page, geometry, op, allowNative: true);
                }
                catch
                {
                    usedFallback = true;
                    foreach (var fallback in ToTapFallbackOps(op))
                        ApplyOp(page, geometry, fallback, allowNative: false);
                }
            }
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
                    AppName = "PdfEditor",
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

        return new ExportResult(usedFallback,
            usedFallback
                ? "No se pudo editar de forma nativa; se aplicó tapado."
                : "Documento exportado correctamente.");
    }

    private static void ValidateBounds(int pageIndex, Rect b, int pageCount, string label)
    {
        if (pageIndex < 0 || pageIndex >= pageCount ||
            !double.IsFinite(b.X + b.Y + b.Width + b.Height) || b.Width <= 0 || b.Height <= 0 ||
            b.X < 0 || b.Y < 0 || b.Right > 1.000001 || b.Bottom > 1.000001)
            throw new InvalidOperationException($"Un {label} está fuera del área visible de la página.");
    }

    private static IEnumerable<ExportOp> ToTapFallbackOps(ExportOp op) => op.Kind switch
    {
        ExportOpKind.NativeTextReplace =>
        [
            op with { Kind = ExportOpKind.TapCover, ColorArgb = 0xFFFFFFFF },
            op with { Kind = ExportOpKind.DrawText }
        ],
        ExportOpKind.NativeImageReplace =>
        [
            op with { Kind = ExportOpKind.TapCover, ColorArgb = 0xFFFFFFFF },
            op with { Kind = ExportOpKind.DrawImage }
        ],
        ExportOpKind.NativeDelete => [op with { Kind = ExportOpKind.TapCover, ColorArgb = 0xFFFFFFFF }],
        _ => [op]
    };

    private static void ApplyOp(PdfPage page, PdfGeometry geometry, ExportOp op, bool allowNative)
    {
        if (op.Kind is ExportOpKind.NativeTextReplace or ExportOpKind.NativeImageReplace or ExportOpKind.NativeDelete)
        {
            if (!allowNative)
                throw new InvalidOperationException("Operación nativa no disponible.");
            // v1 native = cover + redraw (best-effort visual replace). True content-stream
            // surgery is deferred; failures still go through tap fallback above.
            ApplyTapThenContent(page, geometry, op);
            return;
        }

        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.MultiplyTransform(geometry.DisplayToGraphicsMatrix);
        var rect = geometry.ToDisplayRect(op.Bounds);
        switch (op.Kind)
        {
            case ExportOpKind.TapCover:
                graphics.DrawRectangle(new XSolidBrush(ToXColor(op.ColorArgb == 0 ? 0xFFFFFFFF : op.ColorArgb)), rect);
                break;
            case ExportOpKind.DrawText:
                DrawText(graphics, rect, op);
                break;
            case ExportOpKind.DrawImage:
                if (op.PngBytes is { Length: > 0 })
                {
                    using var png = new MemoryStream(op.PngBytes, false);
                    using var image = XImage.FromStream(png);
                    graphics.DrawImage(image, rect);
                }
                break;
        }
    }

    private static void ApplyTapThenContent(PdfPage page, PdfGeometry geometry, ExportOp op)
    {
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.MultiplyTransform(geometry.DisplayToGraphicsMatrix);
        var rect = geometry.ToDisplayRect(op.Bounds);
        graphics.DrawRectangle(XBrushes.White, rect);
        if (op.Kind == ExportOpKind.NativeTextReplace && !string.IsNullOrEmpty(op.Text))
            DrawText(graphics, rect, op);
        if (op.Kind == ExportOpKind.NativeImageReplace && op.PngBytes is { Length: > 0 })
        {
            using var png = new MemoryStream(op.PngBytes, false);
            using var image = XImage.FromStream(png);
            graphics.DrawImage(image, rect);
        }
    }

    private static void DrawText(XGraphics graphics, XRect rect, ExportOp op)
    {
        var size = Math.Max(6, op.FontSizeDip);
        var font = new XFont("Arial", size);
        var brush = new XSolidBrush(ToXColor(op.ColorArgb == 0 ? 0xFF111111 : op.ColorArgb));
        var format = new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.Near };
        graphics.DrawString(op.Text ?? "", font, brush, new XRect(rect.X, rect.Y, rect.Width, rect.Height), format);
    }

    private static XColor ToXColor(uint argb)
    {
        var color = Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
        return XColor.FromArgb(color.A, color.R, color.G, color.B);
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
