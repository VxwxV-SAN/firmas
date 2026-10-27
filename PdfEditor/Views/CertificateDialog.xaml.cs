using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using PdfEditor.Models;
using PdfEditor.Services;
using Microsoft.Win32;

namespace PdfEditor.Views;

public partial class CertificateDialog : DarkWindow
{
    private IReadOnlyList<CertificateEntry> _entries = [];
    private X509Certificate2? _loadedPfx;
    private bool _ready;
    public SigningRequest? Request { get; private set; }

    public CertificateDialog(string? selectedSignatureName)
    {
        InitializeComponent();
        UseAppearance.IsEnabled = selectedSignatureName != null;
        UseAppearance.IsChecked = selectedSignatureName != null;
        AppearanceDescription.Text = selectedSignatureName == null ?
            "Sin firma seleccionada: el certificado firmará el documento sin añadir una marca visible." :
            $"Firma seleccionada: {selectedSignatureName}";
        _ready = true;
        Loaded += (_, _) => RefreshCertificates();
        Closed += (_, _) =>
        {
            PasswordInput.Clear();
            _loadedPfx?.Dispose();
            foreach (var entry in _entries)
                if (!ReferenceEquals(entry.Certificate, Request?.Certificate)) entry.Dispose();
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Seleccionar certificado", Filter = "Certificados PFX/P12|*.pfx;*.p12", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        FileInput.Text = picker.FileName;
        PasswordInput.Clear();
        InvalidatePfx();
    }
    private void InvalidatePfx()
    {
        _loadedPfx?.Dispose();
        _loadedPfx = null;
        if (PfxDetails != null) PfxDetails.Text = "Carga el archivo para revisar el titular y la fecha de validez.";
        UpdateAccept();
    }
    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) InvalidatePfx();
    }
    private void LoadPfx_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FileInput.Text)) throw new InvalidOperationException("Selecciona un archivo PFX o P12.");
            InvalidatePfx();
            _loadedPfx = CertificateService.LoadPfx(FileInput.Text, PasswordInput.Password);
            PfxDetails.Text = $"Titular: {_loadedPfx.GetNameInfo(X509NameType.SimpleName, false)}\n" +
                $"Emisor: {_loadedPfx.GetNameInfo(X509NameType.SimpleName, true)}\n" +
                $"Válido hasta: {_loadedPfx.NotAfter:dd/MM/yyyy}";
            ErrorText.Text = "";
            UpdateAccept();
        }
        catch (CryptographicException) { ErrorText.Text = "No se pudo cargar el certificado. Revisa el archivo y su contraseña."; }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private void RefreshCertificates()
    {
        try
        {
            foreach (var entry in _entries) entry.Dispose();
            _entries = [];
            StoreList.ItemsSource = null;
            _entries = CertificateService.GetWindowsCertificates();
            StoreList.ItemsSource = _entries;
            if (_entries.Count > 0) StoreList.SelectedIndex = 0;
            EmptyStore.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrorText.Text = "";
            UpdateAccept();
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCertificates();
    private void SourceTabs_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateAccept(); }
    private void StoreList_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateAccept(); }
    private void UpdateAccept()
    {
        if (UseButton != null) UseButton.IsEnabled = SourceTabs.SelectedIndex == 0 ? _loadedPfx != null : StoreList.SelectedItem != null;
    }
    private void Use_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var certificate = SourceTabs.SelectedIndex == 0 ? _loadedPfx : (StoreList.SelectedItem as CertificateEntry)?.Certificate;
            if (certificate == null) return;
            CertificateService.EnsureUsable(certificate);
            Request = new SigningRequest(certificate, ReasonInput.Text.Trim(), LocationInput.Text.Trim(), UseAppearance.IsChecked == true);
            if (ReferenceEquals(certificate, _loadedPfx)) _loadedPfx = null;
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
