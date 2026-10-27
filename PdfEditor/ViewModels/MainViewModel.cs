using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PdfEditor.Infrastructure;
using PdfEditor.Models;
using PdfEditor.Services;

namespace PdfEditor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly SignatureStore _store;
    private readonly PdfExporter _exporter = new();
    private readonly List<PlacementState[]> _undo = [];
    private readonly List<PlacementState[]> _redo = [];
    private PlacementState[]? _beforeEdit;
    private PdfSession? _session;
    private int _pageIndex;
    private BitmapSource? _pageBitmap;
    private SignatureAsset? _selectedAsset;
    private SignaturePlacement? _selectedPlacement;
    private bool _isBusy;
    private bool _hasUnsavedChanges;
    private double _zoom = 1;
    private string _status = "Abre un PDF para empezar. Tus firmas se guardan en este equipo.";
    private string _savedState = "";
    private string? _lastExportPath;

    public MainViewModel(IDialogService dialogs, SignatureStore? store = null)
    {
        _dialogs = dialogs;
        _store = store ?? new SignatureStore();
        OpenPdfCommand = new AsyncRelayCommand(OpenPickedAsync, HandleError, () => !IsBusy);
        DrawSignatureCommand = new RelayCommand(() => TryAction(AddDrawnSignature), () => !IsBusy);
        ImportSignatureCommand = new RelayCommand(() => TryAction(ImportPickedSignature), () => !IsBusy);
        RenameSignatureCommand = new RelayCommand(() => TryAction(RenameSelectedSignature),
            () => !IsBusy && SelectedAsset != null);
        DeleteSignatureCommand = new RelayCommand(() => TryAction(DeleteSelectedSignature),
            () => !IsBusy && SelectedAsset != null);
        PlaceSignatureCommand = new RelayCommand(() => PlaceSelectedAtCenter(),
            () => CanEdit && SelectedAsset != null);
        PreviousPageCommand = new AsyncRelayCommand(() => ShowPageAsync(_pageIndex - 1), HandleError,
            () => !IsBusy && HasDocument && _pageIndex > 0);
        NextPageCommand = new AsyncRelayCommand(() => ShowPageAsync(_pageIndex + 1), HandleError,
            () => !IsBusy && HasDocument && _pageIndex < PageCount - 1);
        SaveCopyCommand = new AsyncRelayCommand(() => SaveAsync(false), HandleError,
            () => CanEdit && Placements.Count > 0);
        SignDigitallyCommand = new AsyncRelayCommand(() => SaveAsync(true), HandleError, () => CanEdit);
        RemovePlacementCommand = new RelayCommand(RemoveSelectedPlacement,
            () => CanEdit && SelectedPlacement != null);
        DuplicatePlacementCommand = new RelayCommand(DuplicateSelectedPlacement,
            () => CanEdit && SelectedPlacement != null);
        UndoCommand = new RelayCommand(Undo, () => CanEdit && _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => CanEdit && _redo.Count > 0);
        ZoomInCommand = new RelayCommand(() => Zoom *= 1.15, () => HasDocument && Zoom < 3);
        ZoomOutCommand = new RelayCommand(() => Zoom /= 1.15, () => HasDocument && Zoom > .35);
        OpenExportCommand = new RelayCommand(() => TryAction(() => Process.Start(new ProcessStartInfo(
            LastExportPath!) { UseShellExecute = true })), () => LastExportPath != null && !IsBusy);
    }

    public ObservableCollection<SignatureAsset> Signatures { get; } = [];
    public ObservableCollection<SignaturePlacement> Placements { get; } = [];
    public ObservableCollection<SignaturePlacement> VisiblePlacements { get; } = [];
    public ICommand OpenPdfCommand { get; }
    public ICommand DrawSignatureCommand { get; }
    public ICommand ImportSignatureCommand { get; }
    public ICommand RenameSignatureCommand { get; }
    public ICommand DeleteSignatureCommand { get; }
    public ICommand PlaceSignatureCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand SaveCopyCommand { get; }
    public ICommand SignDigitallyCommand { get; }
    public ICommand RemovePlacementCommand { get; }
    public ICommand DuplicatePlacementCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand OpenExportCommand { get; }

    public bool HasDocument => _session != null;
    public bool CanEdit => !IsBusy && _session?.CanExport == true;
    public bool CanInteract => !IsBusy;
    public int PageCount => _session?.PageCount ?? 0;
    public string FileName => _session == null ? "Ningún documento abierto" : Path.GetFileName(_session.SourcePath);
    public string WindowTitle => $"{(HasUnsavedChanges ? "* " : "")}{(HasDocument ? FileName + " · " : "")}PDF Editor";
    public string PageLabel => HasDocument ? $"Página {_pageIndex + 1} de {PageCount}" : "Sin páginas";
    public string SignatureCountLabel => $"{Signatures.Count} {(Signatures.Count == 1 ? "firma guardada" : "firmas guardadas")}";
    public bool IsLibraryEmpty => Signatures.Count == 0;
    public string PlacementCountLabel => $"{Placements.Count} {(Placements.Count == 1 ? "firma colocada" : "firmas colocadas")}";
    public string SelectionLabel => SelectedPlacement == null ? "Selecciona una firma para moverla o ajustar su tamaño" :
        $"{SelectedPlacement.Asset.Name} · arrastra la esquina para redimensionar";
    public string DocumentHint => _session?.CanExport == false ? _session.RestrictionMessage :
        "Arrastra una firma del panel a la página. También puedes hacer doble clic para colocarla.";
    public double PageWidth => _session?.Pages[_pageIndex].WidthDip ?? 1;
    public double PageHeight => _session?.Pages[_pageIndex].HeightDip ?? 1;
    public BitmapSource? PageBitmap { get => _pageBitmap; private set => Set(ref _pageBitmap, value); }
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!Set(ref _zoom, Math.Clamp(value, .35, 3))) return;
            OnPropertyChanged(nameof(ZoomLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ZoomLabel => $"{Zoom * 100:0} %";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? LastExportPath
    {
        get => _lastExportPath;
        private set { Set(ref _lastExportPath, value); OnPropertyChanged(nameof(HasExport)); }
    }
    public bool HasExport => LastExportPath != null;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set { if (Set(ref _hasUnsavedChanges, value)) OnPropertyChanged(nameof(WindowTitle)); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanInteract));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public SignatureAsset? SelectedAsset
    {
        get => _selectedAsset;
        set { Set(ref _selectedAsset, value); CommandManager.InvalidateRequerySuggested(); }
    }
    public SignaturePlacement? SelectedPlacement
    {
        get => _selectedPlacement;
        set
        {
            if (ReferenceEquals(value, _selectedPlacement)) return;
            if (_selectedPlacement != null) _selectedPlacement.IsSelected = false;
            Set(ref _selectedPlacement, value);
            if (value != null) value.IsSelected = true;
            OnPropertyChanged(nameof(SelectionLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public void Initialize()
    {
        TryAction(() =>
        {
            foreach (var asset in _store.Load()) Signatures.Add(asset);
            SelectedAsset = Signatures.FirstOrDefault();
            NotifyLibrary();
            if (_store.SkippedItems > 0)
                Status = $"Se omitieron {_store.SkippedItems} firmas con archivos incompletos. Biblioteca: {_store.FolderPath}";
        });
    }

    private async Task OpenPickedAsync()
    {
        var path = _dialogs.PickPdf();
        if (path != null) await OpenPathAsync(path);
    }

    public async Task OpenPathAsync(string path)
    {
        if (IsBusy || !ConfirmDiscard()) return;
        PdfSession? candidate = null;
        IsBusy = true;
        Status = "Abriendo documento…";
        try
        {
            var password = "";
            while (true)
            {
                try { candidate = await PdfSession.OpenAsync(path, password); break; }
                catch (Exception ex) when ((uint)ex.HResult == 0x8007052B)
                {
                    var answer = _dialogs.AskPdfPassword();
                    if (answer == null) { Status = "Apertura cancelada."; return; }
                    password = answer;
                }
            }
            var bitmap = await candidate.RenderAsync(0);
            _session?.Dispose();
            _session = candidate;
            candidate = null;
            _pageIndex = 0;
            SelectedPlacement = null;
            UnsubscribePlacements();
            Placements.Clear();
            VisiblePlacements.Clear();
            _undo.Clear();
            _redo.Clear();
            _savedState = "";
            HasUnsavedChanges = false;
            LastExportPath = null;
            PageBitmap = bitmap;
            NotifyDocument();
            Status = _session.CanExport ? "PDF abierto. Coloca tus firmas sobre la página." : _session.RestrictionMessage;
        }
        finally { candidate?.Dispose(); IsBusy = false; }
    }

    private async Task ShowPageAsync(int index)
    {
        if (_session == null || index < 0 || index >= PageCount || IsBusy) return;
        IsBusy = true;
        Status = $"Cargando página {index + 1}…";
        try
        {
            var bitmap = await _session.RenderAsync(index);
            SelectedPlacement = null;
            _pageIndex = index;
            PageBitmap = bitmap;
            RefreshVisiblePlacements();
            NotifyDocument();
            Status = _session.CanExport ? "Página lista. Arrastra una firma para colocarla." : _session.RestrictionMessage;
        }
        finally { IsBusy = false; }
    }

    private void AddDrawnSignature()
    {
        var draft = _dialogs.DrawSignature();
        if (draft != null) AddAsset(draft);
    }

    private void ImportPickedSignature()
    {
        var path = _dialogs.PickImage();
        if (path != null) ImportImagePath(path);
    }

    public void ImportImagePath(string path) => TryAction(() =>
    {
        if (IsBusy) return;
        var draft = _dialogs.ImportSignature(path);
        if (draft != null) AddAsset(draft);
    });

    private void AddAsset(SignatureDraft draft)
    {
        var asset = _store.Add(draft.Name, draft.Kind, draft.Image);
        Signatures.Insert(0, asset);
        SelectedAsset = asset;
        NotifyLibrary();
        Status = $"«{asset.Name}» guardada. Arrástrala a la página o haz doble clic para colocarla.";
    }

    private void RenameSelectedSignature()
    {
        if (SelectedAsset == null) return;
        var answer = _dialogs.AskName(SelectedAsset.Name);
        if (answer != null) _store.Rename(SelectedAsset, answer);
    }

    private void DeleteSelectedSignature()
    {
        if (SelectedAsset == null) return;
        var asset = SelectedAsset;
        if (!_dialogs.Confirm("Eliminar firma", $"¿Eliminar «{asset.Name}» de tu biblioteca?", "Eliminar firma")) return;
        _store.Delete(asset);
        Signatures.Remove(asset);
        SelectedAsset = Signatures.FirstOrDefault();
        NotifyLibrary();
        Status = "Firma eliminada de la biblioteca.";
    }

    public void PlaceSelectedAtCenter()
    {
        if (SelectedAsset != null) PlaceSignature(SelectedAsset, new Point(PageWidth / 2, PageHeight / 2));
    }

    public void PlaceSignature(SignatureAsset asset, Point point)
    {
        if (!CanEdit) return;
        PushUndo(Snapshot());
        var width = Math.Min(210, PageWidth * .32);
        width = Math.Min(width, PageHeight * .3 * asset.AspectRatio);
        var height = width / asset.AspectRatio;
        var bounds = new Rect(Math.Clamp((point.X - width / 2) / PageWidth, 0, 1 - width / PageWidth),
            Math.Clamp((point.Y - height / 2) / PageHeight, 0, 1 - height / PageHeight),
            width / PageWidth, height / PageHeight);
        var placement = new SignaturePlacement(Guid.NewGuid(), asset, _pageIndex, PageWidth, PageHeight, bounds);
        AddPlacement(placement);
        SelectedPlacement = placement;
        NotifyPlacements();
        Status = "Firma colocada. Muévela arrastrando y ajusta el tamaño desde la esquina.";
    }

    private void AddPlacement(SignaturePlacement placement)
    {
        placement.PropertyChanged += PlacementChanged;
        Placements.Add(placement);
        if (placement.PageIndex == _pageIndex) VisiblePlacements.Add(placement);
    }

    public void BeginPlacementEdit() => _beforeEdit = Snapshot();
    public void EndPlacementEdit()
    {
        if (_beforeEdit != null && !_beforeEdit.SequenceEqual(Snapshot())) PushUndo(_beforeEdit);
        _beforeEdit = null;
        NotifyPlacements();
    }

    private void RemoveSelectedPlacement()
    {
        if (SelectedPlacement == null) return;
        PushUndo(Snapshot());
        var placement = SelectedPlacement;
        SelectedPlacement = null;
        placement.PropertyChanged -= PlacementChanged;
        Placements.Remove(placement);
        VisiblePlacements.Remove(placement);
        NotifyPlacements();
    }

    private void DuplicateSelectedPlacement()
    {
        if (SelectedPlacement == null) return;
        PushUndo(Snapshot());
        var current = SelectedPlacement;
        var bounds = current.Bounds;
        bounds.X = Math.Min(1 - bounds.Width, bounds.X + .025);
        bounds.Y = Math.Min(1 - bounds.Height, bounds.Y + .025);
        var copy = new SignaturePlacement(Guid.NewGuid(), current.Asset, current.PageIndex,
            current.PageWidthDip, current.PageHeightDip, bounds);
        AddPlacement(copy);
        SelectedPlacement = copy;
        NotifyPlacements();
    }

    private void Undo()
    {
        _redo.Add(Snapshot());
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(state);
    }

    private void Redo()
    {
        _undo.Add(Snapshot());
        var state = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(state);
    }

    private void PushUndo(PlacementState[] state)
    {
        _undo.Add(state);
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    private PlacementState[] Snapshot() => Placements.Select(p => p.Snapshot()).ToArray();
    private void Restore(PlacementState[] state)
    {
        SelectedPlacement = null;
        UnsubscribePlacements();
        Placements.Clear();
        VisiblePlacements.Clear();
        foreach (var placement in state) AddPlacement(placement.ToPlacement());
        NotifyPlacements();
    }
    private void UnsubscribePlacements()
    {
        foreach (var placement in Placements) placement.PropertyChanged -= PlacementChanged;
    }
    private void PlacementChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignaturePlacement.Bounds)) NotifyPlacements();
    }
    private string StateKey() => string.Join(";", Placements.Select(p =>
        string.Create(CultureInfo.InvariantCulture, $"{p.Id}:{p.PageIndex}:{p.Bounds.X:R},{p.Bounds.Y:R},{p.Bounds.Width:R},{p.Bounds.Height:R}")));
    private void NotifyPlacements()
    {
        HasUnsavedChanges = StateKey() != _savedState;
        OnPropertyChanged(nameof(PlacementCountLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync(bool digitally)
    {
        if (_session == null) return;
        var destination = _dialogs.PickDestination(Path.GetFileNameWithoutExtension(_session.SourcePath) +
            (digitally ? "_firmado_digital.pdf" : "_firmado.pdf"));
        if (destination == null) return;
        if (string.Equals(Path.GetFullPath(destination), _session.SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elige un nombre diferente al PDF abierto para guardar la copia.");
        using var signing = digitally ? _dialogs.ChooseCertificate(SelectedPlacement?.Asset.Name) : null;
        if (digitally && signing == null) return;
        IsBusy = true;
        Status = digitally ? "Aplicando firma digital y guardando PDF…" : "Incorporando firmas y guardando PDF…";
        try
        {
            await _exporter.ExportAsync(_session, Snapshot(), destination, signing, SelectedPlacement?.Id);
            _savedState = StateKey();
            HasUnsavedChanges = false;
            LastExportPath = destination;
            Status = $"{(digitally ? "PDF firmado digitalmente" : "Copia guardada")}: {Path.GetFileName(destination)}";
        }
        finally { IsBusy = false; }
    }

    private void RefreshVisiblePlacements()
    {
        VisiblePlacements.Clear();
        foreach (var placement in Placements.Where(p => p.PageIndex == _pageIndex)) VisiblePlacements.Add(placement);
    }
    private void NotifyLibrary()
    {
        OnPropertyChanged(nameof(SignatureCountLabel));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }
    private void NotifyDocument()
    {
        foreach (var property in new[] { nameof(HasDocument), nameof(CanEdit), nameof(FileName), nameof(WindowTitle),
            nameof(PageLabel), nameof(PageCount), nameof(PageWidth), nameof(PageHeight), nameof(DocumentHint),
            nameof(PlacementCountLabel) }) OnPropertyChanged(property);
        CommandManager.InvalidateRequerySuggested();
    }

    public bool ConfirmDiscard() => !HasUnsavedChanges || _dialogs.Confirm("Firmas sin guardar",
        "Hay cambios en las firmas del documento que todavía no has guardado. ¿Quieres descartarlos?", "Descartar cambios");

    public void HandleError(Exception exception)
    {
        Status = "No se pudo completar la operación.";
        _dialogs.ShowError(exception.Message);
    }
    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { HandleError(ex); }
    }
    public void Dispose() { UnsubscribePlacements(); _session?.Dispose(); }
}
