using System.Windows;

namespace TrayClockApp;

public partial class App
{
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost(this);
        _host.InitAndStart();
    }
}