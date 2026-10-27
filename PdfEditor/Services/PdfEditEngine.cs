using System.Windows;
using PdfEditor.Models;

namespace PdfEditor.Services;

public enum ExportOpKind
{
    TapCover,
    DrawText,
    DrawImage,
    NativeTextReplace,
    NativeImageReplace,
    NativeDelete
}

public sealed record ExportOp(
    ExportOpKind Kind,
    int PageIndex,
    Rect Bounds,
    string? Text,
    double FontSizeDip,
    uint ColorArgb,
    byte[]? PngBytes,
    string? SourceKey);

public sealed record ExportResult(bool UsedTapFallback, string Message);

public static class PdfEditEngine
{
    public static IReadOnlyList<ExportOp> Build(IEnumerable<EditObjectState> objects, bool preferNative = true)
    {
        var ops = new List<ExportOp>();
        foreach (var o in objects)
        {
            if (o.Kind == EditObjectKind.Signature) continue;

            switch (o.Kind)
            {
                case EditObjectKind.ExistingText:
                    if (o.IsDeleted)
                    {
                        ops.Add(CoverOrNativeDelete(o, preferNative));
                        break;
                    }
                    if (o.TextMode == TextEditMode.Native && preferNative)
                        ops.Add(new ExportOp(ExportOpKind.NativeTextReplace, o.PageIndex, o.Bounds, o.Text,
                            o.FontSizeDip, o.ColorArgb, null, o.SourceKey));
                    else
                    {
                        ops.Add(new ExportOp(ExportOpKind.TapCover, o.PageIndex, o.Bounds, null, 0,
                            0xFFFFFFFF, null, o.SourceKey));
                        if (!string.IsNullOrEmpty(o.Text))
                            ops.Add(new ExportOp(ExportOpKind.DrawText, o.PageIndex, o.Bounds, o.Text,
                                o.FontSizeDip, o.ColorArgb, null, o.SourceKey));
                    }
                    break;

                case EditObjectKind.NewText:
                    if (!o.IsDeleted && !string.IsNullOrEmpty(o.Text))
                        ops.Add(new ExportOp(ExportOpKind.DrawText, o.PageIndex, o.Bounds, o.Text,
                            o.FontSizeDip, o.ColorArgb, null, o.SourceKey));
                    break;

                case EditObjectKind.ExistingImage:
                    if (o.IsDeleted)
                    {
                        ops.Add(CoverOrNativeDelete(o, preferNative));
                        break;
                    }
                    if (o.PngBytes is { Length: > 0 })
                    {
                        if (preferNative)
                            ops.Add(new ExportOp(ExportOpKind.NativeImageReplace, o.PageIndex, o.Bounds, null,
                                0, 0, o.PngBytes, o.SourceKey));
                        else
                        {
                            ops.Add(new ExportOp(ExportOpKind.TapCover, o.PageIndex, o.Bounds, null, 0,
                                0xFFFFFFFF, null, o.SourceKey));
                            ops.Add(new ExportOp(ExportOpKind.DrawImage, o.PageIndex, o.Bounds, null, 0, 0,
                                o.PngBytes, o.SourceKey));
                        }
                    }
                    break;

                case EditObjectKind.NewImage:
                    if (!o.IsDeleted && o.PngBytes is { Length: > 0 })
                        ops.Add(new ExportOp(ExportOpKind.DrawImage, o.PageIndex, o.Bounds, null, 0, 0,
                            o.PngBytes, o.SourceKey));
                    break;
            }
        }
        return ops;
    }

    private static ExportOp CoverOrNativeDelete(EditObjectState o, bool preferNative) =>
        preferNative && o.Kind is EditObjectKind.ExistingText or EditObjectKind.ExistingImage
            ? new ExportOp(ExportOpKind.NativeDelete, o.PageIndex, o.Bounds, null, 0, 0, null, o.SourceKey)
            : new ExportOp(ExportOpKind.TapCover, o.PageIndex, o.Bounds, null, 0, 0xFFFFFFFF, null, o.SourceKey);
}
