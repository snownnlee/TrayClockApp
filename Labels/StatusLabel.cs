using System.Windows;
using System.Windows.Media;
using TrayClockApp.Core;
using TrayClockApp.Menus.MoreMenu;

namespace TrayClockApp.Labels;

/// <summary>
/// 两个「更多」功能的公用状态标签：只显示当前生效功能的图标，均未开启时整体隐藏（不占位）。
/// 因 KeepRunCoordinator 为互斥单选（ScreenOn / SystemRun 只能生效其一），任一时刻最多一个图标。
/// </summary>
public class StatusLabel : LabelBase
{
    private KeepAwakeMode _shownMode = KeepAwakeMode.None;
    private bool _subscribed;

    public override void Init()
    {
        base.Init();

        // 只有一个图标，不沿用基类的固定 100 宽度；收起时用 Collapsed 让悬浮条不为其留空位。
        // 图标本身无内边距，故左右留白比其他标签（固定 100 宽、内容居中）更大，避免紧贴功耗标签。
        Text.Width = double.NaN;
        Text.Margin = new Thickness(10, 0, 10, 0);
        Text.Text = string.Empty;
        Text.Visibility = Visibility.Collapsed;
    }

    public override void OnStart()
    {
        base.OnStart();

        if (_subscribed) return;
        EventBus.Register(EventType.StatusChanged, (_, _) => Refresh());
        _subscribed = true;

        Refresh();
    }

    public override void OnStop()
    {
        _subscribed = false;
        base.OnStop();
    }

    /// <summary>每秒兜底刷新（正常路径由 StatusChanged 事件即时触发）。</summary>
    public override void Update() => Refresh();

    private void Refresh()
    {
        var mode = KeepRunCoordinator.Mode;
        if (mode == _shownMode) return;
        _shownMode = mode;

        switch (mode)
        {
            case KeepAwakeMode.ScreenOn:
                Apply(AppIcons.KeepScreenOn, Brushes.Orange);
                break;
            case KeepAwakeMode.SystemRun:
                Apply(AppIcons.KeepSystemRun, Brushes.DeepSkyBlue);
                break;
            case KeepAwakeMode.None:
            default:
                Text.Text = string.Empty;
                Text.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void Apply(string icon, Brush foreground)
    {
        Text.Text = icon;
        // 彩色 emoji 不受 Foreground 影响；此色用于纯文本图标模式下的语义区分
        Text.Foreground = foreground;
        Text.Visibility = Visibility.Visible;
    }
}
