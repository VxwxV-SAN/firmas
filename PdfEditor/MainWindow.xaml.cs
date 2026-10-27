using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfEditor.Controls;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.ViewModels;
using PdfEditor.Views;

namespace PdfEditor;

public partial class MainWindow : DarkWindow
{
    private readonly string? _initialPath;
    private Point _dragStart;
    private SignatureAsset? _dragAsset;
    private bool _fitWidth = true;
    public MainViewModel ViewModel { get; }

    public MainWindow(string? initialPath = null, IDialogService? dialogs = null, SignatureStore? store = null)
    {
        InitializeComponent();
        _initialPath = initialPath;
        ViewModel = new MainViewModel(dialogs ?? new ShellDialogService(this), store);
        DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.PageWidth) && _fitWidth)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitWidth));
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize();
        if (!string.IsNullOrWhiteSpace(_initialPath))
        {
            try { await ViewModel.OpenPathAsync(_initialPath); }
            catch (Exception ex) { ViewModel.HandleError(ex); }
        }
    }

    private void Library_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(SignatureList);
        _dragAsset = (ItemsControl.ContainerFromElement(SignatureList, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as SignatureAsset;
    }
    private void Library_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragAsset == null || !ViewModel.CanEdit) return;
        var point = e.GetPosition(SignatureList);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var asset = _dragAsset;
        _dragAsset = null;
        DragDrop.DoDragDrop(SignatureList, new DataObject(typeof(SignatureAsset), asset), DragDropEffects.Copy);
    }
    private void Library_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_dragAsset != null && ViewModel.PlaceSignatureCommand.CanExecute(null))
            ViewModel.PlaceSignatureCommand.Execute(null);
    }
    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SignatureAsset))) return;
        e.Effects = ViewModel.CanEdit ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Page_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SignatureAsset)) is not SignatureAsset asset) return;
        ViewModel.PlaceSignature(asset, e.GetPosition(PageSurface));
        e.Handled = true;
    }
    private void Page_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<SignaturePlacementControl>(e.OriginalSource as DependencyObject) != null) return;
        if (FindParent<EditObjectControl>(e.OriginalSource as DependencyObject) != null) return;
        ViewModel.HandlePageClick(e.GetPosition(PageSurface));
        e.Handled = true;
    }
    private void Placement_Selected(object? sender, EventArgs e)
    {
        if (ViewModel.CanEdit && sender is SignaturePlacementControl { DataContext: SignaturePlacement placement })
            ViewModel.SelectedPlacement = placement;
    }
    private void Placement_EditStarted(object? sender, EventArgs e) => ViewModel.BeginPlacementEdit();
    private void Placement_EditCompleted(object? sender, EventArgs e) => ViewModel.EndPlacementEdit();
    private void EditObject_Selected(object? sender, EventArgs e)
    {
        if (ViewModel.CanEdit && sender is EditObjectControl { DataContext: PageEditObject obj })
            ViewModel.SelectedEditObject = obj;
    }
    private void EditObject_EditStarted(object? sender, EventArgs e) => ViewModel.BeginEditObjectEdit();
    private void EditObject_EditCompleted(object? sender, EventArgs e) => ViewModel.EndEditObjectEdit();
    private void EditObject_TextEdited(object? sender, EventArgs e) => ViewModel.NotifyTextEdited();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = !ViewModel.IsBusy && SupportedDrop(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        var path = SupportedDrop(e.Data);
        if (path == null || ViewModel.IsBusy) return;
        e.Handled = true;
        try
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                await ViewModel.OpenPathAsync(path);
            else ViewModel.ImportImagePath(path);
        }
        catch (Exception ex) { ViewModel.HandleError(ex); }
    }
    private static string? SupportedDrop(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
        var extension = Path.GetExtension(files[0]).ToLowerInvariant();
        return extension is ".pdf" or ".png" or ".jpg" or ".jpeg" ? files[0] : null;
    }
    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !ViewModel.HasDocument) return;
        _fitWidth = false;
        ViewModel.Zoom *= e.Delta > 0 ? 1.1 : 1 / 1.1;
        e.Handled = true;
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _fitWidth = false; ViewModel.Zoom *= 1.15; }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _fitWidth = false; ViewModel.Zoom /= 1.15; }
    private void FitWidth_Click(object sender, RoutedEventArgs e) { _fitWidth = true; FitWidth(); }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitWidth && ViewModel != null) FitWidth();
    }
    private void FitWidth()
    {
        if (!ViewModel.HasDocument || DocumentScroll.ActualWidth <= 80) return;
        ViewModel.Zoom = (DocumentScroll.ActualWidth - 76) / ViewModel.PageWidth;
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsBusy) { e.Cancel = true; return; }
        if (!ViewModel.ConfirmDiscard()) e.Cancel = true;
    }
    protected override void OnClosed(EventArgs e) { ViewModel.Dispose(); base.OnClosed(e); }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match) return match;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
}
