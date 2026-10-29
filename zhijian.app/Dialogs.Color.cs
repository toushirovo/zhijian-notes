using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace zhijian.app;
internal static partial class Dialogs
{
    public static string? PickColor(Window owner, string initial)
    {
        var w = Shell(owner, "文字颜色", 640, 620);
        var body = Frame(w, "知笺  /  文字样式", "给重点一点颜色", "选中文字后应用颜色；光标位于已着色文字中时，可修改整段颜色。", out var actions);
        var example = Label("重点内容 · 让想法更清晰", 23); example.FontWeight = FontWeights.SemiBold;
        body.Children.Add(new Border { Child = example, Background = Brush(w, "PanelBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18) });
        body.Children.Add(Label("常用颜色", 13)); var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) }; body.Children.Add(swatches);
        var hex = new TextBox { Name = "ColorHex", Text = initial, Margin = new Thickness(0, 6, 0, 12), MaxLength = 7 };
        System.Windows.Automation.AutomationProperties.SetName(hex, "十六进制颜色，例如#E06C75");
        body.Children.Add(Label("自定义色值  #RRGGBB", 13)); body.Children.Add(hex);
        var channels = new List<Slider>(); bool updating = false;
        var status = Label("", 12);
        foreach (var name in new[] { "红 R", "绿 G", "蓝 B" })
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; var label = Label(name, 12); label.Width = 50; row.Children.Add(label);
            var slider = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(slider, name); channels.Add(slider); row.Children.Add(slider); body.Children.Add(row);
            slider.ValueChanged += (_, _) => { if (!updating && channels.Count == 3) hex.Text = $"#{(int)channels[0].Value:X2}{(int)channels[1].Value:X2}{(int)channels[2].Value:X2}"; };
        }
        body.Children.Add(status); string? result = null;
        Action(w, actions, "取消", () => w.Close(), false, true);
        Action(w, actions, "恢复默认色", () => { result = ""; w.Close(); });
        var apply = Action(w, actions, "应用颜色", () => { result = hex.Text.ToUpperInvariant(); w.Close(); }, true);
        void Update()
        {
            bool valid = System.Text.RegularExpressions.Regex.IsMatch(hex.Text, "^#[0-9a-fA-F]{6}$"); apply.IsEnabled = valid;
            status.Text = valid ? "正文以所选颜色预览；深色主题下建议选择较亮的颜色。" : "请输入#加6位十六进制字符，例如#E06C75。";
            if (!valid) return;
            var color = (Color)ColorConverter.ConvertFromString(hex.Text); example.Foreground = new SolidColorBrush(color);
            updating = true; channels[0].Value = color.R; channels[1].Value = color.G; channels[2].Value = color.B; updating = false;
        }
        foreach (var value in new[] { "#236B57", "#5BB98C", "#3B82F6", "#A78BFA", "#E06C75", "#E7A23B", "#94A3B8", "#E2E8F0" })
        {
            var button = new Button { Width = 42, Height = 36, Padding = new Thickness(0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)), ToolTip = value, Margin = new Thickness(0, 0, 8, 6) };
            System.Windows.Automation.AutomationProperties.SetName(button, "选择颜色 " + value);
            button.Click += (_, _) => hex.Text = value; swatches.Children.Add(button);
        }
        hex.TextChanged += (_, _) => Update(); Update(); w.ShowDialog(); return result;
    }
}
