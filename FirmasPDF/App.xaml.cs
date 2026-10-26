using System.Globalization;
using System.Windows;

namespace FirmasPDF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
        base.OnStartup(e);
        var window = new MainWindow(e.Args.FirstOrDefault());
        MainWindow = window;
        window.Show();
    }
}
