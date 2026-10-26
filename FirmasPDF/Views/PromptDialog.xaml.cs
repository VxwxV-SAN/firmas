using System.Windows;

namespace FirmasPDF.Views;

public enum PromptKind { Notice, Confirm, Text, Password }

public partial class PromptDialog : DarkWindow
{
    private readonly PromptKind _kind;
    public string ResultText { get; private set; } = "";

    public PromptDialog(string title, string message, PromptKind kind = PromptKind.Notice,
        string acceptLabel = "Aceptar", string initialText = "")
    {
        InitializeComponent();
        _kind = kind;
        Title = title;
        Heading.Text = title;
        Description.Text = message;
        AcceptButton.Content = acceptLabel;
        CancelButton.Visibility = kind == PromptKind.Notice ? Visibility.Collapsed : Visibility.Visible;
        NameInput.Visibility = kind == PromptKind.Text ? Visibility.Visible : Visibility.Collapsed;
        SecretInput.Visibility = kind == PromptKind.Password ? Visibility.Visible : Visibility.Collapsed;
        NameInput.Text = initialText;
        Loaded += (_, _) =>
        {
            if (kind == PromptKind.Text) { NameInput.Focus(); NameInput.SelectAll(); }
            else if (kind == PromptKind.Password) SecretInput.Focus();
            else AcceptButton.Focus();
        };
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        ResultText = _kind == PromptKind.Password ? SecretInput.Password : NameInput.Text.Trim();
        if (_kind == PromptKind.Text && ResultText.Length == 0)
        {
            ErrorText.Text = "Escribe un nombre para la firma.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        SecretInput.Clear();
        DialogResult = true;
    }

    public static void Error(Window owner, string message) =>
        new PromptDialog("No se pudo completar", message) { Owner = owner }.ShowDialog();
}
