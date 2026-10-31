using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace zhijian.app;
public partial class MainWindow
{
    private async Task CheckTools(string output, Action<bool, string> check)
    {
        check(!NoteMeta.Text.Contains(current!.ShortId), "日常笔记信息不显示内部编号");
        string[] expected = ["标题", "粗体", "斜体", "无序列表", "有序列表", "引用", "代码块", "文字颜色…", "笔记双链…", "网页链接", "本地图片…", "粘贴剪贴板图片", "仅编辑", "分屏", "仅预览", "导出本篇…", "另存正文…", "笔记信息", "移入回收站", "恢复笔记", "永久删除…"];
        var menus = new[] { FormatToolbar, MediaToolbar, ViewMenuButton, NoteActionsButton };
        var items = menus.SelectMany(b => b.ContextMenu.Items.OfType<MenuItem>()).ToArray();
        check(expected.All(label => items.Any(i => Equals(i.Header, label))), "四组菜单保留全部21项操作");
        var original = BodyBox.Text; BodyBox.Text = "菜单选区"; BodyBox.SelectAll();
        OpenTools_Click(FormatToolbar, new RoutedEventArgs());
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check(FormatToolbar.ContextMenu.IsOpen, "左键打开格式菜单");
        items.Single(i => Equals(i.Header, "粗体")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        FormatToolbar.ContextMenu.IsOpen = false;
        check(BodyBox.Text == "**菜单选区**", "格式菜单操作保留正文选区");
        BodyBox.Text = original; await SaveCurrent();
        foreach (var mode in new[] { "edit", "preview", "split" })
        {
            LayoutMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, mode)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(editorLayout == mode && LayoutMenu.Items.OfType<MenuItem>().Count(i => i.IsChecked) == 1, "视图菜单切换及选中标记 " + mode);
        }
        foreach (var darkMode in new[] { false, true })
        {
            ApplyTheme(darkMode); OpenTools_Click(FormatToolbar, new RoutedEventArgs());
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var menu = FormatToolbar.ContextMenu; menu.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)menu.ActualWidth, (int)menu.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, darkMode ? "menu-dark.png" : "menu-light.png"))) png.Save(stream);
            check(menu.ActualHeight > 200 && menu.ActualWidth >= 200, "分组菜单布局 " + (darkMode ? "深色" : "浅色")); menu.IsOpen = false;
        }
        ApplyTheme(false);
    }
}
