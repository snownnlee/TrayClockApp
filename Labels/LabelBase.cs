using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using TrayClockApp.Core;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace TrayClockApp.Labels;

public abstract class LabelBase : ILabel
{
    protected readonly TextBlock Text = new();

    private bool Started { get; set; }

    public virtual void Init()
    {
        Text.FontFamily = new FontFamily(Constants.EmojiFont);
        Text.FontSize = 16;
        Text.Foreground = Brushes.White;
        Text.TextAlignment = TextAlignment.Center;
        Text.VerticalAlignment = VerticalAlignment.Center;
        Text.Margin = new Thickness(3, 0, 3, 0);
        Text.Width = 100;
        Text.Height = Constants.DefaultComponentHeight;
    }

    public virtual void AddUiMenuItem(ContextMenu menu)
    {
    }

    public virtual void AddTrayMenuItem(ContextMenuStrip menu)
    {
    }

    public virtual void OnMainWindowCreated(MainWindow window)
    {
    }

    public virtual void OnLoadConfig(JsonObject config)
    {
    }

    public virtual void OnSaveConfig(JsonObject config)
    {
    }

    public virtual void OnStart()
    {
        if (Started) return;
        StartOnce();
        Started = true;
    }

    public virtual void OnStop()
    {
        Started = false;
    }

    public TextBlock GetTextBlock()
    {
        return Text;
    }

    protected virtual void StartOnce()
    {
    }

    public virtual void Update()
    {
    }
}