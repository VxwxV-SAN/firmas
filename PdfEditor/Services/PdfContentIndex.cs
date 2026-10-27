using System.Windows;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Services;

public sealed record TextHit(string Key, int PageIndex, string Text, Rect Bounds,
    double FontSizeDip, bool NativeCandidate);

public sealed record ImageHit(string Key, int PageIndex, Rect Bounds);

public sealed class PdfContentIndex
{
    private readonly Dictionary<int, List<TextHit>> _text = new();
    private readonly Dictionary<int, List<ImageHit>> _images = new();

    public static PdfContentIndex Build(byte[] pdfBytes, string? password)
    {
        var index = new PdfContentIndex();
        try
        {
            using var doc = string.IsNullOrEmpty(password)
                ? PdfDocument.Open(pdfBytes)
                : PdfDocument.Open(pdfBytes, new ParsingOptions { Password = password });
            var pageNumber = 0;
            foreach (var page in doc.GetPages())
            {
                var w = Math.Max(page.Width, 1);
                var h = Math.Max(page.Height, 1);
                foreach (var group in GroupWordsIntoLines(page.GetWords().ToList()))
                {
                    if (group.Count == 0) continue;
                    var text = string.Join(" ", group.Select(g => g.Text));
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var minX = group.Min(g => g.BoundingBox.Left);
                    var minY = group.Min(g => g.BoundingBox.Bottom);
                    var maxX = group.Max(g => g.BoundingBox.Right);
                    var maxY = group.Max(g => g.BoundingBox.Top);
                    var width = Math.Max(maxX - minX, 1);
                    var height = Math.Max(maxY - minY, 1);
                    var bounds = new Rect(
                        Clamp01(minX / w),
                        Clamp01(1 - maxY / h),
                        Clamp01(width / w),
                        Clamp01(height / h));
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    var fontSize = group
                        .SelectMany(g => g.Letters)
                        .Select(l => l.PointSize)
                        .DefaultIfEmpty(12)
                        .Average();
                    var key = $"t:{pageNumber}:{minX:F1}:{minY:F1}:{text.GetHashCode(StringComparison.Ordinal):X}";
                    GetOrAdd(index._text, pageNumber).Add(new TextHit(key, pageNumber, text, bounds,
                        fontSize, nativeCandidate: text.Length > 0));
                }

                var imgI = 0;
                foreach (var img in page.GetImages())
                {
                    try
                    {
                        var box = img.Bounds;
                        var bounds = new Rect(
                            Clamp01(box.Left / w),
                            Clamp01(1 - box.Top / h),
                            Clamp01(Math.Max(box.Right - box.Left, 1) / w),
                            Clamp01(Math.Max(box.Top - box.Bottom, 1) / h));
                        if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                        GetOrAdd(index._images, pageNumber).Add(new ImageHit($"i:{pageNumber}:{imgI++}",
                            pageNumber, bounds));
                    }
                    catch
                    {
                        // Image bounds unavailable for this XObject; skip.
                    }
                }
                pageNumber++;
            }
        }
        catch
        {
            // Index is best-effort; editing can still add overlays.
        }
        return index;
    }

    public IReadOnlyList<TextHit> GetText(int pageIndex) =>
        _text.TryGetValue(pageIndex, out var list) ? list : Array.Empty<TextHit>();

    public IReadOnlyList<ImageHit> GetImages(int pageIndex) =>
        _images.TryGetValue(pageIndex, out var list) ? list : Array.Empty<ImageHit>();

    public int TextCount => _text.Values.Sum(v => v.Count);
    public int ImageCount => _images.Values.Sum(v => v.Count);

    private static List<T> GetOrAdd<T>(Dictionary<int, List<T>> map, int key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }
        return list;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static List<List<Word>> GroupWordsIntoLines(List<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom)
                     .ThenBy(w => w.BoundingBox.Left))
        {
            var placed = false;
            foreach (var line in lines)
            {
                var refY = line[0].BoundingBox.Bottom;
                if (Math.Abs(word.BoundingBox.Bottom - refY) <= 3)
                {
                    line.Add(word);
                    placed = true;
                    break;
                }
            }
            if (!placed) lines.Add([word]);
        }
        foreach (var line in lines)
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }

}
