using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FirmasPDF.Models;
using FirmasPDF.Services;

namespace FirmasPDF.Views;

public partial class SignatureDialog : DarkWindow
{
    private readonly BitmapSource? _imported;
    private readonly List<StrokeCollection> _undo = [];
    private readonly List<StrokeCollection> _redo = [];
    private StrokeCollection _lastStrokes = new();
    private bool _restoring;
    public SignatureDraft? Draft { get; private set; }

    public SignatureDialog(BitmapSource? imported = null, string suggestedName = "Mi firma")
    {
        InitializeComponent();
        _imported = imported;
        Title = Heading.Text = imported == null ? "Crea tu firma" : "Importa tu firma";
        Subtitle.Text = imported == null ? "Usa el ratón o un lápiz. La tendrás lista para todos tus documentos." :
            "Revisa la imagen antes de añadirla a tu biblioteca.";
        NameInput.Text = suggestedName.Length > 80 ? suggestedName[..80] : suggestedName;
        DrawingSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromRgb(25, 32, 46), Width = 3, Height = 3, FitToCurve = true,
            IgnorePressure = false
        };
        DrawingSurface.Strokes.StrokesChanged += Strokes_Changed;
        if (imported != null)
        {
            DrawingPanel.Visibility = DrawingToolbar.Visibility = DrawingFooter.Visibility = Visibility.Collapsed;
            RemoveWhite.Visibility = ImportPreview.Visibility = Visibility.Visible;
            UpdatePreview();
        }
        UpdateDrawingHistory();
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void Strokes_Changed(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_restoring)
        {
            _undo.Add(_lastStrokes);
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
        }
        _lastStrokes = DrawingSurface.Strokes.Clone();
        UpdateDrawingHistory();
    }
    private void UpdateDrawingHistory()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        DrawingHint.Visibility = DrawingSurface.Strokes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RestoreStrokes(StrokeCollection strokes)
    {
        _restoring = true;
        DrawingSurface.Strokes.Clear();
        DrawingSurface.Strokes.Add(strokes.Clone());
        _restoring = false;
        _lastStrokes = DrawingSurface.Strokes.Clone();
        UpdateDrawingHistory();
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        _redo.Add(DrawingSurface.Strokes.Clone());
        var strokes = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreStrokes(strokes);
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        _undo.Add(DrawingSurface.Strokes.Clone());
        var strokes = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreStrokes(strokes);
    }
    private void Clear_Click(object sender, RoutedEventArgs e) => DrawingSurface.Strokes.Clear();
    private void Color_Click(object sender, RoutedEventArgs e) =>
        DrawingSurface.DefaultDrawingAttributes.Color = (Color)ColorConverter.ConvertFromString((string)((Button)sender).Tag);
    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DrawingSurface == null) return;
        DrawingSurface.DefaultDrawingAttributes.Width = DrawingSurface.DefaultDrawingAttributes.Height = e.NewValue;
    }
    private void RemoveWhite_Changed(object sender, RoutedEventArgs e)
    {
        if (_imported != null) UpdatePreview();
    }
    private void UpdatePreview()
    {
        try
        {
            ImportPreview.Source = SignatureImageService.PrepareImport(_imported!, RemoveWhite.IsChecked == true);
            ErrorText.Text = "";
        }
        catch (Exception ex) { ImportPreview.Source = null; ErrorText.Text = ex.Message; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NameInput.Text.Trim();
            if (name.Length == 0) throw new InvalidOperationException("Escribe un nombre para tu firma.");
            var image = _imported == null ? SignatureImageService.FromStrokes(DrawingSurface.Strokes) :
                SignatureImageService.PrepareImport(_imported, RemoveWhite.IsChecked == true);
            Draft = new SignatureDraft(name, image, _imported == null ? SignatureKind.Drawn : SignatureKind.Imported);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
