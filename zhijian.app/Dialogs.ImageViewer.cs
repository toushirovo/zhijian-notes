using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace zhijian.app;

internal static partial class Dialogs
{
    public static int? ImageViewer(Window owner, byte[] bytes, bool editable)
    {
        var bitmap = ImageCodec.Decode(bytes);
        var w = Shell(owner, "查看图片", 900, 720);
        var body = Frame(w, "知笺  /  图片", "图片预览", "拖动缩放滑块查看细节；显示尺寸只调整笔记中的呈现，原图保持不变。", out var actions);
        var picture = new Image { Source = bitmap, Stretch = Stretch.Uniform, Width = 650, HorizontalAlignment = HorizontalAlignment.Left };
        var viewer = new ScrollViewer { Content = picture, Height = 330, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(viewer);
        var zoom = new Slider { Minimum = 25, Maximum = 200, Value = 100, Margin = new Thickness(0, 16, 0, 10), TickFrequency = 25, ToolTip = "查看缩放 25%～200%" };
        zoom.ValueChanged += (_, _) => picture.Width = 650 * zoom.Value / 100;
        body.Children.Add(zoom);
        int? result = null;
        if (editable)
        {
            body.Children.Add(Label("笔记中的显示宽度", 13));
            var widths = new ComboBox { ItemsSource = new[] { "自适应", "小图 · 240", "中图 · 420", "大图 · 720", "超大 · 1000" }, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 12), MinHeight = 32 };
            body.Children.Add(widths);
            Action(w, actions, "应用显示尺寸", () => { result = new[] { 0, 240, 420, 720, 1000 }[widths.SelectedIndex]; w.Close(); }, true);
        }
        Action(w, actions, "关闭", () => w.Close(), cancel: true);
        w.ShowDialog(); return result;
    }
}
