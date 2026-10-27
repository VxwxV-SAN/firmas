using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PdfEditor.Infrastructure;

namespace PdfEditor.Models;

public enum EditObjectKind { ExistingText, ExistingImage, NewText, NewImage, Signature }
public enum TextEditMode { Native, Tap }

public sealed class PageEditObject : ObservableObject
{
    private Rect _bounds;
    private bool _isSelected;
    private string? _text;
    private double _fontSizeDip;
    private uint _colorArgb;
    private TextEditMode _textMode;
    private byte[]? _pngBytes;
    private string? _sourceKey;
    private bool _isDeleted;

    public PageEditObject(Guid id, EditObjectKind kind, int pageIndex,
        double pageWidthDip, double pageHeightDip, Rect bounds,
        string? text = null, double fontSizeDip = 16, uint colorArgb = 0xFF111111,
        TextEditMode textMode = TextEditMode.Tap, byte[]? pngBytes = null,
        string? sourceKey = null, SignatureAsset? signatureAsset = null)
    {
        Id = id;
        Kind = kind;
        PageIndex = pageIndex;
        PageWidthDip = pageWidthDip;
        PageHeightDip = pageHeightDip;
        _bounds = bounds;
        _text = text;
        OriginalText = text;
        OriginalBounds = bounds;
        _fontSizeDip = fontSizeDip;
        _colorArgb = colorArgb;
        _textMode = textMode;
        _pngBytes = pngBytes;
        _sourceKey = sourceKey;
        SignatureAsset = signatureAsset;
    }

    public string? OriginalText { get; private set; }
    public Rect OriginalBounds { get; private set; }
    internal void RestoreOriginal(string? text, Rect bounds)
    {
        OriginalText = text;
        OriginalBounds = bounds;
    }

    public Guid Id { get; }
    public EditObjectKind Kind { get; }
    public int PageIndex { get; }
    public double PageWidthDip { get; }
    public double PageHeightDip { get; }
    public SignatureAsset? SignatureAsset { get; }
    public string? SourceKey { get => _sourceKey; set => Set(ref _sourceKey, value); }
    public Rect Bounds
    {
        get => _bounds;
        set
        {
            if (!Set(ref _bounds, value)) return;
            OnPropertyChanged(nameof(CanvasLeft));
            OnPropertyChanged(nameof(CanvasTop));
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
        }
    }
    public double CanvasLeft => Bounds.X * PageWidthDip;
    public double CanvasTop => Bounds.Y * PageHeightDip;
    public double CanvasWidth => Bounds.Width * PageWidthDip;
    public double CanvasHeight => Bounds.Height * PageHeightDip;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string? Text { get => _text; set => Set(ref _text, value); }
    public double FontSizeDip { get => _fontSizeDip; set => Set(ref _fontSizeDip, value); }
    public uint ColorArgb { get => _colorArgb; set => Set(ref _colorArgb, value); }
    public TextEditMode TextMode { get => _textMode; set => Set(ref _textMode, value); }
    public byte[]? PngBytes
    {
        get => _pngBytes;
        set
        {
            if (!Set(ref _pngBytes, value)) return;
            OnPropertyChanged(nameof(Image));
        }
    }
    public bool IsDeleted { get => _isDeleted; set => Set(ref _isDeleted, value); }
    public string ModeBadge => TextMode == TextEditMode.Native ? "Nativo" : "Tapar";
    public bool IsTextKind => Kind is EditObjectKind.ExistingText or EditObjectKind.NewText;
    public bool IsImageKind => Kind is EditObjectKind.ExistingImage or EditObjectKind.NewImage;
    public bool IsModified => Kind switch
    {
        EditObjectKind.NewText => !IsDeleted && !string.IsNullOrWhiteSpace(Text),
        EditObjectKind.NewImage => !IsDeleted && PngBytes is { Length: > 0 },
        EditObjectKind.ExistingText => IsDeleted || Text != OriginalText || Bounds != OriginalBounds,
        EditObjectKind.ExistingImage => IsDeleted || PngBytes is { Length: > 0 } || Bounds != OriginalBounds,
        _ => false
    };
    public BitmapSource? Image
    {
        get
        {
            if (_pngBytes is not { Length: > 0 }) return null;
            try
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(_pngBytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }
    }

    public void MoveBy(double dx, double dy) => Bounds = new Rect(
        Math.Clamp(Bounds.X + dx / PageWidthDip, 0, 1 - Bounds.Width),
        Math.Clamp(Bounds.Y + dy / PageHeightDip, 0, 1 - Bounds.Height),
        Bounds.Width, Bounds.Height);

    public void ResizeBy(double dx, double dy)
    {
        var maxWidth = Math.Min(PageWidthDip - CanvasLeft, PageHeightDip - CanvasTop);
        var width = Math.Clamp(CanvasWidth + dx, Math.Min(24, maxWidth), maxWidth);
        var height = Math.Clamp(CanvasHeight + dy, Math.Min(16, PageHeightDip - CanvasTop),
            PageHeightDip - CanvasTop);
        Bounds = new Rect(Bounds.X, Bounds.Y, width / PageWidthDip, height / PageHeightDip);
    }

    public EditObjectState Snapshot() => new(Id, Kind, PageIndex, PageWidthDip, PageHeightDip,
        Bounds, Text, FontSizeDip, ColorArgb, TextMode, PngBytes, SourceKey, IsDeleted,
        SignatureAsset, OriginalText, OriginalBounds);
}

public sealed record EditObjectState(
    Guid Id, EditObjectKind Kind, int PageIndex, double PageWidthDip, double PageHeightDip,
    Rect Bounds, string? Text, double FontSizeDip, uint ColorArgb, TextEditMode TextMode,
    byte[]? PngBytes, string? SourceKey, bool IsDeleted, SignatureAsset? SignatureAsset,
    string? OriginalText, Rect OriginalBounds)
{
    public PageEditObject ToObject()
    {
        var obj = new PageEditObject(Id, Kind, PageIndex, PageWidthDip, PageHeightDip,
            Bounds, Text, FontSizeDip, ColorArgb, TextMode, PngBytes, SourceKey, SignatureAsset)
        { IsDeleted = IsDeleted };
        obj.RestoreOriginal(OriginalText, OriginalBounds);
        return obj;
    }
}

public sealed record DocumentEditSnapshot(EditObjectState[] Objects, PlacementState[] Placements);
