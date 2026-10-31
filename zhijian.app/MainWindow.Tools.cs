using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace zhijian.app;
public partial class MainWindow
{
    private void OpenTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        var menu = button.ContextMenu;
        menu.Resources = Resources;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.VerticalOffset = 6;
        menu.IsOpen = true;
    }
    private void NoteInfo_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        Dialogs.Text(this, "笔记信息", $"标题：{current.Title}\n创建时间：{current.CreatedAt?.ToLocalTime():yyyy-MM-dd HH:mm}\n更新时间：{current.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n\n内部编号（用于双链定位）：\n{current.Id}");
    }
}
