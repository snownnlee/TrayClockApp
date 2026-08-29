using System.Windows.Controls;
using TrayClockApp.Core;

namespace TrayClockApp.Labels;

public interface ILabel : IComponent
{
    public TextBlock GetTextBlock();

    public void Update();
}