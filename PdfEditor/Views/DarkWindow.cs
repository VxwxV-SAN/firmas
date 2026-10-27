using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PdfEditor.Views;

public class DarkWindow : Window
{
    public DarkWindow() => SetResourceReference(StyleProperty, typeof(Window));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
