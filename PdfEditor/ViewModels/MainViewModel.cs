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
    private readonly List<DocumentEditSnapshot> _undo = [];
    private readonly List<DocumentEditSnapshot> _redo = [];
    private DocumentEditSnapshot? _beforeEdit;
    private PdfSession? _session;
    private int _pageIndex;
    private BitmapSource? _pageBitmap;
    private SignatureAsset? _selectedAsset;
    private SignaturePlacement? _selectedPlacement;
    private PageEditObject? _selectedEditObject;
    private bool _isBusy;
    private bool _hasUnsavedChanges;
    private double _zoom = 1;
    private string _status = "Abre un PDF para empezar. Puedes editar texto e imágenes y firmar.";
    private string _savedState = "";
    private string? _lastExportPath;
    private EditorTool _selectedTool = EditorTool.Select;
    private int _leftTab;

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
        SaveCopyCommand = new AsyncRelayCommand(() => SaveAsync(false), HandleError, () => CanSave);
        SignDigitallyCommand = new AsyncRelayCommand(() => SaveAsync(true), HandleError, () => CanEdit);
        RemovePlacementCommand = new RelayCommand(RemoveSelected, () => CanEdit && HasSelection);
        DuplicatePlacementCommand = new RelayCommand(DuplicateSelectedPlacement,
            () => CanEdit && SelectedPlacement != null);
        UndoCommand = new RelayCommand(Undo, () => CanEdit && _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => CanEdit && _redo.Count > 0);
        ZoomInCommand = new RelayCommand(() => Zoom *= 1.15, () => HasDocument && Zoom < 3);
        ZoomOutCommand = new RelayCommand(() => Zoom /= 1.15, () => HasDocument && Zoom > .35);
        OpenExportCommand = new RelayCommand(() => TryAction(() => Process.Start(new ProcessStartInfo(
            LastExportPath!) { UseShellExecute = true })), () => LastExportPath != null && !IsBusy);
        SetToolCommand = new RelayCommand<EditorTool>(tool => SelectedTool = tool);
        SetLeftTabCommand = new RelayCommand<int>(tab => LeftTab = tab);
        ReplaceSelectedImageCommand = new RelayCommand(() => TryAction(ReplaceSelectedImage),
            () => CanEdit && SelectedEditObject?.IsImageKind == true);
        DeleteSelectedEditCommand = new RelayCommand(DeleteSelectedEdit,
            () => CanEdit && SelectedEditObject != null);
    }

    public ObservableCollection<SignatureAsset> Signatures { get; } = [];
    public ObservableCollection<SignaturePlacement> Placements { get; } = [];
    public ObservableCollection<SignaturePlacement> VisiblePlacements { get; } = [];
    public ObservableCollection<PageEditObject> EditObjects { get; } = [];
    public ObservableCollection<PageEditObject> VisibleEditObjects { get; } = [];
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
    public ICommand SetToolCommand { get; }
    public ICommand SetLeftTabCommand { get; }
    public ICommand ReplaceSelectedImageCommand { get; }
    public ICommand DeleteSelectedEditCommand { get; }

    public bool HasDocument => _session != null;
    public bool CanEdit => !IsBusy && _session?.CanExport == true;
    public bool CanSave => CanEdit && (Placements.Count > 0 || EditObjects.Any(o => o.IsModified));
    public bool CanInteract => !IsBusy;
    public bool HasSelection => SelectedPlacement != null || SelectedEditObject != null;
    public int PageCount => _session?.PageCount ?? 0;
    public string FileName => _session == null ? "Ningún documento abierto" : Path.GetFileName(_session.SourcePath);
    public string WindowTitle => $"{(HasUnsavedChanges ? "* " : "")}{(HasDocument ? FileName + " · " : "")}PDF Editor";
    public string PageLabel => HasDocument ? $"Página {_pageIndex + 1} de {PageCount}" : "Sin páginas";
    public string SignatureCountLabel => $"{Signatures.Count} {(Signatures.Count == 1 ? "firma guardada" : "firmas guardadas")}";
    public bool IsLibraryEmpty => Signatures.Count == 0;
    public string PlacementCountLabel
    {
        get
        {
            var edits = EditObjects.Count(o => !o.IsDeleted && o.Kind != EditObjectKind.Signature);
            return $"{Placements.Count} firmas · {edits} ediciones";
        }
    }
    public string SelectionLabel
    {
        get
        {
            if (SelectedEditObject != null)
            {
                return SelectedEditObject.IsTextKind
                    ? $"Texto · modo {SelectedEditObject.ModeBadge}"
                    : "Imagen seleccionada · puedes reemplazarla o eliminarla";
            }
            return SelectedPlacement == null
                ? "Selecciona un objeto para editarlo"
                : $"{SelectedPlacement.Asset.Name} · arrastra la esquina para redimensionar";
        }
    }
    public string DocumentHint => _session?.CanExport == false ? _session.RestrictionMessage :
        "Usa las herramientas de Editar para texto e imágenes, o la pestaña Firmas.";
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
            OnPropertyChanged(nameof(CanSave));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public EditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (!Set(ref _selectedTool, value)) return;
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsTextTool));
            OnPropertyChanged(nameof(IsImageTool));
            OnPropertyChanged(nameof(IsSignatureTool));
            if (value == EditorTool.Signature) LeftTab = 1;
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsSelectTool => SelectedTool == EditorTool.Select;
    public bool IsTextTool => SelectedTool == EditorTool.Text;
    public bool IsImageTool => SelectedTool == EditorTool.Image;
    public bool IsSignatureTool => SelectedTool == EditorTool.Signature;
    public int LeftTab
    {
        get => _leftTab;
        set
        {
            if (!Set(ref _leftTab, value)) return;
            OnPropertyChanged(nameof(IsEditTab));
            OnPropertyChanged(nameof(IsSignaturesTab));
        }
    }
    public bool IsEditTab => LeftTab == 0;
    public bool IsSignaturesTab => LeftTab == 1;
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
            if (value != null)
            {
                value.IsSelected = true;
                if (_selectedEditObject != null) { _selectedEditObject.IsSelected = false; _selectedEditObject = null; OnPropertyChanged(nameof(SelectedEditObject)); }
            }
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(HasSelection));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public PageEditObject? SelectedEditObject
    {
        get => _selectedEditObject;
        set
        {
            if (ReferenceEquals(value, _selectedEditObject)) return;
            if (_selectedEditObject != null) _selectedEditObject.IsSelected = false;
            Set(ref _selectedEditObject, value);
            if (value != null)
            {
                value.IsSelected = true;
                if (_selectedPlacement != null) { _selectedPlacement.IsSelected = false; _selectedPlacement = null; OnPropertyChanged(nameof(SelectedPlacement)); }
            }
            OnPropertyChanged(nameof(SelectionLabel));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowTextProperties));
            OnPropertyChanged(nameof(ShowImageProperties));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool ShowTextProperties => SelectedEditObject?.IsTextKind == true;
    public bool ShowImageProperties => SelectedEditObject?.IsImageKind == true;

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
            SelectedEditObject = null;
            UnsubscribePlacements();
            UnsubscribeEdits();
            Placements.Clear();
            VisiblePlacements.Clear();
            EditObjects.Clear();
            VisibleEditObjects.Clear();
            _undo.Clear();
            _redo.Clear();
            _savedState = "";
            HasUnsavedChanges = false;
            LastExportPath = null;
            PageBitmap = bitmap;
            EnsureIndexedObjectsForPage(0);
            RefreshVisibleEdits();
            NotifyDocument();
            var index = _session.ContentIndex;
            Status = _session.CanExport
                ? $"PDF abierto. {index.TextCount} textos · {index.ImageCount} imágenes detectados. Edición nativa o tapado según el contenido."
                : _session.RestrictionMessage;
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
            SelectedEditObject = null;
            _pageIndex = index;
            PageBitmap = bitmap;
            EnsureIndexedObjectsForPage(index);
            RefreshVisiblePlacements();
            RefreshVisibleEdits();
            NotifyDocument();
            Status = _session.CanExport ? "Página lista." : _session.RestrictionMessage;
        }
        finally { IsBusy = false; }
    }

    private void EnsureIndexedObjectsForPage(int pageIndex)
    {
        if (_session?.ContentIndex == null) return;
        var page = _session.Pages[pageIndex];
        foreach (var hit in _session.ContentIndex.GetText(pageIndex))
        {
            if (EditObjects.Any(o => o.SourceKey == hit.Key)) continue;
            AddEditObject(new PageEditObject(Guid.NewGuid(), EditObjectKind.ExistingText, pageIndex,
                page.WidthDip, page.HeightDip, hit.Bounds, text: hit.Text, fontSizeDip: hit.FontSizeDip,
                textMode: hit.NativeCandidate ? TextEditMode.Native : TextEditMode.Tap, sourceKey: hit.Key),
                trackUndo: false);
        }
        foreach (var hit in _session.ContentIndex.GetImages(pageIndex))
        {
            if (EditObjects.Any(o => o.SourceKey == hit.Key)) continue;
            AddEditObject(new PageEditObject(Guid.NewGuid(), EditObjectKind.ExistingImage, pageIndex,
                page.WidthDip, page.HeightDip, hit.Bounds, sourceKey: hit.Key), trackUndo: false);
        }
    }

    public void HandlePageClick(Point point)
    {
        if (!CanEdit) return;
        if (SelectedTool == EditorTool.Text)
        {
            PushUndo(Snapshot());
            var width = Math.Min(280, PageWidth * .45) / PageWidth;
            var height = Math.Min(36, PageHeight * .08) / PageHeight;
            var bounds = new Rect(
                Math.Clamp(point.X / PageWidth - width / 2, 0, 1 - width),
                Math.Clamp(point.Y / PageHeight - height / 2, 0, 1 - height),
                width, height);
            var obj = new PageEditObject(Guid.NewGuid(), EditObjectKind.NewText, _pageIndex,
                PageWidth, PageHeight, bounds, text: "Escribe aquí…", fontSizeDip: 16,
                textMode: TextEditMode.Tap);
            AddEditObject(obj);
            SelectedEditObject = obj;
            Status = "Texto añadido. Edítalo en el cuadro.";
            return;
        }
        if (SelectedTool == EditorTool.Image)
        {
            var path = _dialogs.PickImage();
            if (path == null) return;
            TryAction(() => PlaceNewImage(path, point));
            return;
        }
        if (SelectedTool == EditorTool.Select)
        {
            SelectedPlacement = null;
            SelectedEditObject = null;
        }
    }

    private void PlaceNewImage(string path, Point point)
    {
        PushUndo(Snapshot());
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var aspect = frame.PixelWidth / (double)Math.Max(1, frame.PixelHeight);
        var widthDip = Math.Min(220, PageWidth * .35);
        widthDip = Math.Min(widthDip, PageHeight * .4 * aspect);
        var heightDip = widthDip / aspect;
        var png = EncodePng(frame);
        var bounds = new Rect(
            Math.Clamp((point.X - widthDip / 2) / PageWidth, 0, 1 - widthDip / PageWidth),
            Math.Clamp((point.Y - heightDip / 2) / PageHeight, 0, 1 - heightDip / PageHeight),
            widthDip / PageWidth, heightDip / PageHeight);
        var obj = new PageEditObject(Guid.NewGuid(), EditObjectKind.NewImage, _pageIndex,
            PageWidth, PageHeight, bounds, pngBytes: png);
        AddEditObject(obj);
        SelectedEditObject = obj;
        Status = "Imagen colocada.";
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private void ReplaceSelectedImage()
    {
        if (SelectedEditObject is not { IsImageKind: true } obj) return;
        var path = _dialogs.PickImage();
        if (path == null) return;
        PushUndo(Snapshot());
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        obj.PngBytes = EncodePng(decoder.Frames[0]);
        if (obj.Kind == EditObjectKind.ExistingImage)
            obj.TextMode = TextEditMode.Tap;
        NotifyEdits();
        Status = "Imagen reemplazada. Se exportará tapando el original si hace falta.";
    }

    private void DeleteSelectedEdit()
    {
        if (SelectedEditObject == null) return;
        PushUndo(Snapshot());
        var obj = SelectedEditObject;
        SelectedEditObject = null;
        if (obj.Kind is EditObjectKind.ExistingText or EditObjectKind.ExistingImage)
        {
            obj.IsDeleted = true;
            VisibleEditObjects.Remove(obj);
        }
        else
        {
            obj.PropertyChanged -= EditChanged;
            EditObjects.Remove(obj);
            VisibleEditObjects.Remove(obj);
        }
        NotifyEdits();
        Status = "Elemento eliminado de la edición.";
    }

    private void RemoveSelected()
    {
        if (SelectedEditObject != null) { DeleteSelectedEdit(); return; }
        if (SelectedPlacement == null) return;
        PushUndo(Snapshot());
        var placement = SelectedPlacement;
        SelectedPlacement = null;
        placement.PropertyChanged -= PlacementChanged;
        Placements.Remove(placement);
        VisiblePlacements.Remove(placement);
        NotifyPlacements();
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
        if (SelectedTool == EditorTool.Image && CanEdit && HasDocument)
        {
            PlaceNewImage(path, new Point(PageWidth / 2, PageHeight / 2));
            return;
        }
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

    private void AddEditObject(PageEditObject obj, bool trackUndo = true)
    {
        obj.PropertyChanged += EditChanged;
        EditObjects.Add(obj);
        if (!obj.IsDeleted && obj.PageIndex == _pageIndex) VisibleEditObjects.Add(obj);
        if (trackUndo) NotifyEdits();
    }

    public void BeginPlacementEdit() => _beforeEdit = Snapshot();
    public void BeginEditObjectEdit() => _beforeEdit = Snapshot();
    public void EndPlacementEdit() => EndAnyEdit();
    public void EndEditObjectEdit() => EndAnyEdit();
    public void NotifyTextEdited() => NotifyEdits();

    private void EndAnyEdit()
    {
        if (_beforeEdit != null && !SameSnapshot(_beforeEdit, Snapshot())) PushUndo(_beforeEdit);
        _beforeEdit = null;
        NotifyPlacements();
        NotifyEdits();
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

    private void PushUndo(DocumentEditSnapshot state)
    {
        _undo.Add(state);
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    private DocumentEditSnapshot Snapshot() => new(
        EditObjects.Select(o => o.Snapshot()).ToArray(),
        Placements.Select(p => p.Snapshot()).ToArray());

    private static bool SameSnapshot(DocumentEditSnapshot a, DocumentEditSnapshot b) =>
        a.Placements.SequenceEqual(b.Placements) &&
        a.Objects.Select(StateKey).SequenceEqual(b.Objects.Select(StateKey));

    private static string StateKey(EditObjectState o) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{o.Id}:{o.Kind}:{o.PageIndex}:{o.Bounds}:{o.Text}:{o.IsDeleted}:{o.TextMode}:{o.PngBytes?.Length ?? 0}");

    private void Restore(DocumentEditSnapshot state)
    {
        SelectedPlacement = null;
        SelectedEditObject = null;
        UnsubscribePlacements();
        UnsubscribeEdits();
        Placements.Clear();
        VisiblePlacements.Clear();
        EditObjects.Clear();
        VisibleEditObjects.Clear();
        foreach (var placement in state.Placements) AddPlacement(placement.ToPlacement());
        foreach (var edit in state.Objects) AddEditObject(edit.ToObject(), trackUndo: false);
        RefreshVisibleEdits();
        NotifyPlacements();
        NotifyEdits();
    }

    private void UnsubscribePlacements()
    {
        foreach (var placement in Placements) placement.PropertyChanged -= PlacementChanged;
    }
    private void UnsubscribeEdits()
    {
        foreach (var edit in EditObjects) edit.PropertyChanged -= EditChanged;
    }
    private void PlacementChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignaturePlacement.Bounds)) NotifyPlacements();
    }
    private void EditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageEditObject.Bounds) or nameof(PageEditObject.Text)
            or nameof(PageEditObject.IsDeleted) or nameof(PageEditObject.PngBytes)
            or nameof(PageEditObject.FontSizeDip) or nameof(PageEditObject.ColorArgb))
            NotifyEdits();
    }

    private string StateKey() => string.Join("|",
        string.Join(";", Placements.Select(p =>
            string.Create(CultureInfo.InvariantCulture, $"{p.Id}:{p.PageIndex}:{p.Bounds.X:R},{p.Bounds.Y:R},{p.Bounds.Width:R},{p.Bounds.Height:R}"))),
        string.Join(";", EditObjects.Select(o => StateKey(o.Snapshot()))));

    private void NotifyPlacements()
    {
        HasUnsavedChanges = StateKey() != _savedState;
        OnPropertyChanged(nameof(PlacementCountLabel));
        OnPropertyChanged(nameof(CanSave));
        CommandManager.InvalidateRequerySuggested();
    }

    private void NotifyEdits()
    {
        HasUnsavedChanges = StateKey() != _savedState;
        OnPropertyChanged(nameof(PlacementCountLabel));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(CanSave));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync(bool digitally)
    {
        if (_session == null) return;
        var destination = _dialogs.PickDestination(Path.GetFileNameWithoutExtension(_session.SourcePath) +
            (digitally ? "_firmado_digital.pdf" : "_editado.pdf"));
        if (destination == null) return;
        if (string.Equals(Path.GetFullPath(destination), _session.SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Elige un nombre diferente al PDF abierto para guardar la copia.");
        using var signing = digitally ? _dialogs.ChooseCertificate(SelectedPlacement?.Asset.Name) : null;
        if (digitally && signing == null) return;
        IsBusy = true;
        Status = digitally ? "Aplicando firma digital y guardando PDF…" : "Aplicando ediciones y guardando PDF…";
        try
        {
            var edits = EditObjects.Where(o => o.IsModified).Select(o => o.Snapshot()).ToArray();
            var result = await _exporter.ExportAsync(_session, edits, Snapshot().Placements, destination,
                signing, SelectedPlacement?.Id);
            _savedState = StateKey();
            HasUnsavedChanges = false;
            LastExportPath = destination;
            Status = result.UsedTapFallback
                ? result.Message
                : $"{(digitally ? "PDF firmado digitalmente" : "Copia guardada")}: {Path.GetFileName(destination)}";
        }
        finally { IsBusy = false; }
    }

    private void RefreshVisiblePlacements()
    {
        VisiblePlacements.Clear();
        foreach (var placement in Placements.Where(p => p.PageIndex == _pageIndex)) VisiblePlacements.Add(placement);
    }

    private void RefreshVisibleEdits()
    {
        VisibleEditObjects.Clear();
        foreach (var edit in EditObjects.Where(o => o.PageIndex == _pageIndex && !o.IsDeleted))
            VisibleEditObjects.Add(edit);
    }

    private void NotifyLibrary()
    {
        OnPropertyChanged(nameof(SignatureCountLabel));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }
    private void NotifyDocument()
    {
        foreach (var property in new[] { nameof(HasDocument), nameof(CanEdit), nameof(CanSave), nameof(FileName),
            nameof(WindowTitle), nameof(PageLabel), nameof(PageCount), nameof(PageWidth), nameof(PageHeight),
            nameof(DocumentHint), nameof(PlacementCountLabel) }) OnPropertyChanged(property);
        CommandManager.InvalidateRequerySuggested();
    }

    public bool ConfirmDiscard() => !HasUnsavedChanges || _dialogs.Confirm("Cambios sin guardar",
        "Hay cambios en el documento que todavía no has guardado. ¿Quieres descartarlos?", "Descartar cambios");

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
    public void Dispose()
    {
        UnsubscribePlacements();
        UnsubscribeEdits();
        _session?.Dispose();
    }
}
