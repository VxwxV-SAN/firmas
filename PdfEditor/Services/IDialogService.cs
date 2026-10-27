using System.Windows.Media.Imaging;
using PdfEditor.Models;

namespace PdfEditor.Services;

public sealed record SignatureDraft(string Name, BitmapSource Image, SignatureKind Kind);

public interface IDialogService
{
    string? PickPdf();
    string? PickImage();
    string? PickDestination(string suggestedName);
    SignatureDraft? DrawSignature();
    SignatureDraft? ImportSignature(string path);
    string? AskName(string currentName);
    string? AskPdfPassword();
    SigningRequest? ChooseCertificate(string? selectedSignatureName);
    bool Confirm(string title, string message, string acceptLabel);
    void ShowError(string message);
}
