using System.IO;
using System.Windows;
using PdfEditor.Models;
using PdfEditor.Views;
using Microsoft.Win32;

namespace PdfEditor.Services;

public sealed class ShellDialogService(Window owner) : IDialogService
{
    public string? PickPdf()
    {
        var dialog = new OpenFileDialog { Title = "Abrir documento PDF", Filter = "Documentos PDF|*.pdf", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public string? PickImage()
    {
        var dialog = new OpenFileDialog { Title = "Importar firma", Filter = "Imágenes de firma|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public string? PickDestination(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar copia del PDF", Filter = "Documento PDF|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = suggestedName, OverwritePrompt = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    public SignatureDraft? DrawSignature()
    {
        var dialog = new SignatureDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Draft : null;
    }
    public SignatureDraft? ImportSignature(string path)
    {
        var dialog = new SignatureDialog(SignatureImageService.Load(path), Path.GetFileNameWithoutExtension(path)) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Draft : null;
    }
    public string? AskName(string currentName)
    {
        var dialog = new PromptDialog("Renombrar firma", "Elige un nombre para identificarla en tu biblioteca.",
            PromptKind.Text, "Guardar nombre", currentName) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }
    public string? AskPdfPassword()
    {
        var dialog = new PromptDialog("PDF protegido", "Introduce la contraseña del documento para abrirlo.",
            PromptKind.Password, "Abrir PDF") { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }
    public SigningRequest? ChooseCertificate(string? selectedSignatureName)
    {
        var dialog = new CertificateDialog(selectedSignatureName) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Request : null;
    }
    public bool Confirm(string title, string message, string acceptLabel) =>
        new PromptDialog(title, message, PromptKind.Confirm, acceptLabel) { Owner = owner }.ShowDialog() == true;
    public void ShowError(string message) => PromptDialog.Error(owner, message);
}
