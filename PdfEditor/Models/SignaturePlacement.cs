using System.Windows;
using PdfEditor.Infrastructure;

namespace PdfEditor.Models;

public sealed class SignaturePlacement(Guid id, SignatureAsset asset, int pageIndex,
    double pageWidthDip, double pageHeightDip, Rect bounds) : ObservableObject
{
    private Rect _bounds = bounds;
    private bool _isSelected;

    public Guid Id { get; } = id;
    public SignatureAsset Asset { get; } = asset;
    public int PageIndex { get; } = pageIndex;
    public double PageWidthDip { get; } = pageWidthDip;
    public double PageHeightDip { get; } = pageHeightDip;
    // Relative to the visible, rotated page; independent of zoom and screen DPI.
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

    public void MoveBy(double dx, double dy) => Bounds = new Rect(
        Math.Clamp(Bounds.X + dx / PageWidthDip, 0, 1 - Bounds.Width),
        Math.Clamp(Bounds.Y + dy / PageHeightDip, 0, 1 - Bounds.Height),
        Bounds.Width, Bounds.Height);

    public void ResizeBy(double dx, double dy)
    {
        var delta = Math.Abs(dx) >= Math.Abs(dy * Asset.AspectRatio) ? dx : dy * Asset.AspectRatio;
        var maxWidth = Math.Min(PageWidthDip - CanvasLeft,
            (PageHeightDip - CanvasTop) * Asset.AspectRatio);
        var width = Math.Clamp(CanvasWidth + delta, Math.Min(24, maxWidth), maxWidth);
        Bounds = new Rect(Bounds.X, Bounds.Y, width / PageWidthDip,
            width / Asset.AspectRatio / PageHeightDip);
    }

    public PlacementState Snapshot() => new(Id, Asset, PageIndex, PageWidthDip, PageHeightDip, Bounds);
}

public sealed record PlacementState(Guid Id, SignatureAsset Asset, int PageIndex,
    double PageWidthDip, double PageHeightDip, Rect Bounds)
{
    public SignaturePlacement ToPlacement() => new(Id, Asset, PageIndex, PageWidthDip, PageHeightDip, Bounds);
}
