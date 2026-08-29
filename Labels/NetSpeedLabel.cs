using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayClockApp.Core;
using TrayClockApp.Infra;
using WF = System.Windows.Forms;

namespace TrayClockApp.Labels;

public class NetSpeedLabel : LabelBase
{
    private const string Emoji = "\U0001F310 ";
    private const string InitSpeed = Emoji + "--- ↑  --- ↓";
    private const string SelectedInterface = "selectedInterface";

    private readonly Lock _speedLock = new();

    private string _cachedSpeed = InitSpeed;
    private long _currentDownload;
    private NetworkInterface? _currentInterface;
    private string? _currentInterfaceName;
    private long _currentTime;
    private long _currentUpload;
    private long _lastDownload;
    private long _lastTime;
    private long _lastUpload;

    private Timer? _sampler;

    public override void Init()
    {
        base.Init();
        Text.Text = InitSpeed;
        Text.Foreground = Brushes.Magenta;
        Text.Width = 180;
        PickDefaultInterface();
    }

    private void PickDefaultInterface()
    {
        NetworkInterface? candidate = null;
        foreach (var ni in GetUsableInterfaces())
        {
            if (candidate == null)
            {
                candidate = ni;
                continue;
            }

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var hasIpv4Addr = !FormatAddresses(ni, AddressFamily.InterNetwork).Equals("[]");
            var hasIpv6Addr = !FormatAddresses(ni, AddressFamily.InterNetworkV6).Equals("[]");

            if (hasIpv4Addr && hasIpv6Addr) candidate = ni;
        }

        if (candidate == null) return;
        _currentInterface = candidate;
        _currentInterfaceName = candidate.Name;
    }

    private static List<NetworkInterface> GetUsableInterfaces()
    {
        try
        {
            return
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.GetIPProperties().UnicastAddresses.Any(a =>
                                     a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            ];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"枚举网络接口失败: {ex.Message}");
            return [];
        }
    }

    public override void Update()
    {
        Text.Text = string.IsNullOrEmpty(_currentInterfaceName) ? InitSpeed : _cachedSpeed;
    }

    public override void OnLoadConfig(JsonObject config)
    {
        var node = config[SelectedInterface];
        if (node is null) return;
        var name = node.GetValue<string>();
        if (!UpdateCurrentInterface(name)) WindowUtil.ShowToast("未查找到的网络接口：" + name);
    }

    public override void OnSaveConfig(JsonObject config)
    {
        if (!string.IsNullOrEmpty(_currentInterfaceName)) config[SelectedInterface] = _currentInterfaceName;
    }

    public override void AddUiMenuItem(ContextMenu menu)
    {
        menu.Items.Add(new Separator());
        var interfaceMenu = new MenuItem { Header = "网络接口 >>> " };
        var interfaces = GetUsableInterfaces();
        if (interfaces.Count == 0)
            interfaceMenu.Items.Add(new MenuItem { Header = "未检测到网络接口", IsEnabled = false });
        else
            foreach (var ni in interfaces)
            {
                var item = new MenuItem
                {
                    Header = ni.Name,
                    IsChecked = ni.Name.Equals(_currentInterfaceName)
                };
                BuildDropDown(item, ni, () => menu.IsOpen = false);
                interfaceMenu.Items.Add(item);
            }

        menu.Items.Add(interfaceMenu);
    }

    public override void AddTrayMenuItem(WF.ContextMenuStrip menu)
    {
        menu.Items.Add(new WF.ToolStripSeparator());
        var interfaceMenu = new WF.ToolStripMenuItem("网络接口 >>> ");
        var interfaces = GetUsableInterfaces().ToList();
        if (interfaces.Count == 0)
            interfaceMenu.DropDownItems.Add(new WF.ToolStripMenuItem("未检测到网络接口") { Enabled = false });
        else
            foreach (var ni in interfaces)
            {
                var item = new WF.ToolStripMenuItem(ni.Name) { Checked = ni.Name == _currentInterfaceName };
                BuildDropDown(item, ni, () => CloseWholeMenu(item));
                interfaceMenu.DropDownItems.Add(item);
            }

        menu.Items.Add(interfaceMenu);
    }

    private void BuildDropDown(MenuItem item, NetworkInterface ni, Action closeMenu)
    {
        item.Items.Add(InfoItemWpf("名称：" + ni.Name));
        item.Items.Add(InfoItemWpf("描述：" + ni.Description));
        item.Items.Add(InfoItemWpf("IPv4地址：" + FormatAddresses(ni, AddressFamily.InterNetwork)));
        item.Items.Add(InfoItemWpf("IPv6地址：" + FormatAddresses(ni, AddressFamily.InterNetworkV6)));
        item.Items.Add(InfoItemWpf("Mac地址：" + FormatMac(ni)));

        item.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestorMenuItem(e.OriginalSource as DependencyObject) != item) return;

            SwitchInterface(ni.Name);
            closeMenu();
            e.Handled = true;
        };
    }

    private static MenuItem? FindAncestorMenuItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is MenuItem mi) return mi;
            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void BuildDropDown(WF.ToolStripMenuItem item, NetworkInterface ni, Action closeMenu)
    {
        item.DropDownItems.Add(InfoItemTray("名称：" + ni.Name));
        item.DropDownItems.Add(InfoItemTray("描述：" + ni.Description));
        item.DropDownItems.Add(InfoItemTray("IPv4地址：" + FormatAddresses(ni, AddressFamily.InterNetwork)));
        item.DropDownItems.Add(InfoItemTray("IPv6地址：" + FormatAddresses(ni, AddressFamily.InterNetworkV6)));
        item.DropDownItems.Add(InfoItemTray("Mac地址：" + FormatMac(ni)));

        item.MouseDown += (_, _) =>
        {
            SwitchInterface(ni.Name);
            closeMenu();
        };
    }

    private void SwitchInterface(string name)
    {
        if (UpdateCurrentInterface(name))
        {
            EventBus.Publish(EventType.ConfigChanged);
            WindowUtil.ShowToast("网络接口已切换为：" + name);
        }
        else
        {
            WindowUtil.ShowToast("切换网络接口失败：" + name);
        }
    }

    private bool UpdateCurrentInterface(string name)
    {
        if (name.Equals(_currentInterfaceName)) return true;

        var theInterface = FindInterface(name);
        if (theInterface == null) return false;

        lock (_speedLock)
        {
            _currentInterface = theInterface;
            _currentInterfaceName = name;
            _cachedSpeed = InitSpeed;
            _lastUpload = _lastDownload = _lastTime = 0;
            _currentUpload = _currentDownload = _currentTime = 0;
        }

        return true;
    }

    private static NetworkInterface? FindInterface(string name)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.Name == name)
                    return ni;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"查找网络接口失败: {ex.Message}");
        }

        return null;
    }

    private static string FormatAddresses(NetworkInterface ni, AddressFamily family)
    {
        var addresses = ni.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == family)
            .Select(a => a.Address.ToString())
            .ToArray();
        return "[" + string.Join(", ", addresses) + "]";
    }

    private static string FormatMac(NetworkInterface ni)
    {
        var bytes = ni.GetPhysicalAddress().GetAddressBytes();
        return string.Join(":", bytes.Select(b => b.ToString("x2")));
    }

    protected override void StartOnce()
    {
        _sampler = new Timer(_ => CalculateSpeed(), null, 0, 1000);
    }

    public override void OnStop()
    {
        _sampler?.Dispose();
        _sampler = null;
        base.OnStop();
    }

    private void CalculateSpeed()
    {
        lock (_speedLock)
        {
            var name = _currentInterfaceName;
            if (string.IsNullOrEmpty(name)) return;

            var stats = _currentInterface!.GetIPv4Statistics();
            _currentUpload = stats.BytesSent;
            _currentDownload = stats.BytesReceived;
            _currentTime = Environment.TickCount64;

            if (_lastTime != 0)
            {
                var dt = _currentTime - _lastTime;
                if (dt > 0)
                {
                    var uploadSpeed = (_currentUpload - _lastUpload) * 1000 / dt;
                    var downloadSpeed = (_currentDownload - _lastDownload) * 1000 / dt;
                    _cachedSpeed = Emoji + FormatSpeed(uploadSpeed) + "↑  " + FormatSpeed(downloadSpeed) + "↓";
                }
            }

            _lastUpload = _currentUpload;
            _lastDownload = _currentDownload;
            _lastTime = _currentTime;
        }
    }

    private static string FormatSpeed(long bytes)
    {
        return bytes switch
        {
            < Constants.DataSize1Kb => bytes + "B",
            < Constants.DataSize1Mb => $"{bytes / (double)Constants.DataSize1Kb:F1}KB",
            < Constants.DataSize1Gb => $"{bytes / (double)Constants.DataSize1Mb:F1}MB",
            _ => $"{bytes / (double)Constants.DataSize1Gb:F1}GB"
        };
    }

    private static WF.ToolStripMenuItem InfoItemTray(string text)
    {
        var item = new WF.ToolStripMenuItem(text);
        item.Click += (_, _) => CopyInfo(text);
        return item;
    }

    private static MenuItem InfoItemWpf(string text)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => CopyInfo(text);
        return item;
    }

    private static void CopyInfo(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"复制失败: {ex.Message}");
        }

        WindowUtil.ShowToast("已复制：" + text);
    }

    private static void CloseWholeMenu(WF.ToolStripDropDownItem item)
    {
        var current = item;
        var visited = new HashSet<WF.ToolStripDropDownItem> { current };
        while (current.OwnerItem is WF.ToolStripDropDownItem parent && visited.Add(parent))
            current = parent;

        (current.GetCurrentParent() as WF.ToolStripDropDown)?.Close();
    }
}