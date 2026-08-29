using System.Windows.Forms;
using TrayClockApp.Labels;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace TrayClockApp.Menus;

public class LabelComponentMenuItem(List<ILabel> labels) : MenuItemBase
{
    public override void AddUiMenuItem(ContextMenu menu)
    {
        foreach (var label in labels) label.AddUiMenuItem(menu);
    }

    public override void AddTrayMenuItem(ContextMenuStrip menu)
    {
        foreach (var label in labels) label.AddTrayMenuItem(menu);
    }
}