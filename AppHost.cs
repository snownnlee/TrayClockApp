using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using System.Windows.Threading;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using TrayClockApp.Labels;
using TrayClockApp.Menus;
using Application = System.Windows.Application;

namespace TrayClockApp;

public class AppHost(Application application)
{
    private readonly NotifyIcon _trayIcon = new();
    private List<ILabel> _labels = [];
    private List<IMenuItem> _menuItems = [];
    private DispatcherTimer? _timer;
    private MainWindow? _window;

    public void InitAndStart()
    {
        _labels =
        [
            new TimeLabel(),
            new NetSpeedLabel(),
            new MemoryLabel(),
            new BatteryLabel()
        ];
        _menuItems =
        [
            new ShowHideMenuItem(),
            new ToFrontMenuItem(),
            new InteractionMenuItem(),
            new UiMenuItem(),
            new OpacityMenuItem(),
            new LabelComponentMenuItem(_labels),
            new MoreMenuItem(),
            new RestartMenuItem(),
            new ExitMenuItem()
        ];

        foreach (var label in _labels) label.Init();
        foreach (var menuItem in _menuItems) menuItem.Init();

        try
        {
            CreateMainWindow();

            CreateUiTimer();

            TrayIconManager.Create(_trayIcon, _menuItems, _window!);

            LoadConfig();

            EventBus.Register(EventType.ConfigChanged, (_, _) => SaveConfig());
            EventBus.Register(EventType.Restart, (_, _) => Restart());
            EventBus.Register(EventType.Exit, (_, _) => Exit());

            Start();

            Console.WriteLine("托盘时钟程序启动成功");
        }
        catch (Exception ex)
        {
            WindowUtil.ShowErrorDialog("启动失败", "程序启动失败: " + ex.Message);
            application.Shutdown(2);
        }
    }

    private void CreateMainWindow()
    {
        _window = new MainWindow(_labels, _menuItems);

        foreach (var label in _labels) label.OnMainWindowCreated(_window);
        foreach (var menuItem in _menuItems) menuItem.OnMainWindowCreated(_window);
    }

    private void CreateUiTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            foreach (var label in _labels) label.Update();
        };
    }

    private void Start()
    {
        foreach (var label in _labels) label.OnStart();
        foreach (var menuItem in _menuItems) menuItem.OnStart();
        _timer!.Start();
        _window!.Show();
    }

    private void LoadConfig()
    {
        var config = ConfigStore.Load();
        if (config is null) return;
        foreach (var label in _labels) label.OnLoadConfig(config);
        foreach (var menuItem in _menuItems) menuItem.OnLoadConfig(config);
        Console.WriteLine($"配置已从 {Constants.ConfigFile} 加载");
    }

    private void SaveConfig()
    {
        var config = new JsonObject();
        foreach (var label in _labels) label.OnSaveConfig(config);
        foreach (var menuItem in _menuItems) menuItem.OnSaveConfig(config);
        ConfigStore.Save(config);
        Console.WriteLine($"配置已保存到: {Constants.ConfigFile}");
    }

    private void Restart()
    {
        Destroy();

        Console.WriteLine("应用准备重启！");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true
        });
        application.Shutdown();
    }

    private void Exit()
    {
        Destroy();

        Console.WriteLine("应用正常退出！");
        application.Shutdown();
    }

    private void Destroy()
    {
        Console.WriteLine("开始执行退出操作");

        SaveConfig();
        Console.WriteLine("配置已保存");

        foreach (var label in _labels) label.OnStop();
        foreach (var menuItem in _menuItems) menuItem.OnStop();
        Console.WriteLine("所有组件已停止");

        if (_timer is not null)
        {
            _timer.Stop();
            Console.WriteLine("定时器已停止");
        }

        _window?.Close();
        Console.WriteLine("窗口已销毁");

        TrayIconManager.Dispose(_trayIcon);
        Console.WriteLine("托盘图标已移除");

        EventBus.Clear();
        Console.WriteLine("退出操作完成");
    }
}