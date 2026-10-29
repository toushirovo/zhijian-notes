using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zhijian.Core;

namespace zhijian.app;

internal static partial class Dialogs
{
    private static Brush Brush(Window owner, string key) => (Brush)owner.FindResource(key);
    private static TextBlock Label(string text, double size = 14) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.65 };
    private static Window Shell(Window owner, string title, double width, double height) => new()
    {
        Owner = owner, Title = title, Width = Math.Min(width, SystemParameters.WorkArea.Width),
        Height = Math.Min(height, SystemParameters.WorkArea.Height), WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = Brush(owner, "CanvasBrush"), Foreground = Brush(owner, "TextBrush"),
        FontFamily = owner.FontFamily, FontSize = 14, Resources = owner.Resources, ShowInTaskbar = false,
        ResizeMode = ResizeMode.CanResize, MinWidth = 380, MinHeight = 300
    };
    private static StackPanel Frame(Window w, string eyebrow, string title, string description, out WrapPanel actions)
    {
        var root = new DockPanel(); w.Content = root;
        var header = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
        var caption = Label(eyebrow, 11); caption.Foreground = Brush(w, "AccentTextBrush"); header.Children.Add(caption);
        var heading = Label(title, 26); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 5, 0, 6); header.Children.Add(heading);
        var detail = Label(description, 13); detail.Foreground = Brush(w, "MutedBrush"); header.Children.Add(detail);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 14, 24, 14) };
        var footer = new Border { Child = actions, Background = Brush(w, "PanelBrush"), BorderBrush = Brush(w, "LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var content = new StackPanel { Margin = new Thickness(28, 0, 28, 24) };
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return content;
    }
    private static void Card(Window w, Panel body, string title, string text)
    {
        var stack = new StackPanel();
        var heading = Label(title, 15); heading.FontWeight = FontWeights.SemiBold; stack.Children.Add(heading);
        var copy = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0), VerticalContentAlignment = VerticalAlignment.Top };
        stack.Children.Add(copy);
        body.Children.Add(new Border { Child = stack, Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(10), Background = Brush(w, "PanelBrush"), BorderBrush = Brush(w, "LineBrush"), BorderThickness = new Thickness(1) });
    }
    private static Button Action(Window w, Panel actions, string text, Action action, bool primary = false, bool cancel = false)
    {
        var button = new Button { Content = text, MinHeight = 38, Margin = new Thickness(4), IsCancel = cancel };
        if (primary) button.Style = (Style)w.FindResource("PrimaryButton");
        button.Click += (_, _) => action(); actions.Children.Add(button); return button;
    }
    public static void Text(Window owner, string title, string text)
    {
        var w = Shell(owner, title, 700, 540);
        var body = Frame(w, "知笺  /  提示", title, "查看详细信息后继续操作。", out var actions);
        Card(w, body, "详细信息", text);
        Action(w, actions, "知道了", () => w.Close(), true, true); w.ShowDialog();
    }
    public static bool Confirm(Window owner, string title, string description, string details, string accept = "继续", bool danger = false)
    {
        var w = Shell(owner, title, 620, 440);
        var body = Frame(w, danger ? "知笺  /  不可撤回的操作" : "知笺  /  操作确认", title, description, out var actions);
        Card(w, body, danger ? "请确认影响范围" : "操作详情", details);
        bool confirmed = false;
        var cancel = Action(w, actions, "取消", () => w.Close(), false, true);
        var submit = Action(w, actions, accept, () => { confirmed = true; w.Close(); }, true);
        if (danger) submit.Background = new SolidColorBrush(Color.FromRgb(177, 55, 64));
        w.Loaded += (_, _) => cancel.Focus(); w.ShowDialog(); return confirmed;
    }
    public static void Report(Window owner, BatchReport report)
    {
        var skipped = report.Items.Count(i => i.Status == "SKIPPED");
        var warnings = report.Items.Count(i => i.Status == "SUCCESS" && !string.IsNullOrWhiteSpace(i.Message));
        var title = report.FailedCount > 0 ? report.Operation + "有失败项" : skipped > 0 ? report.Operation + "已结束，部分跳过" : warnings > 0 ? report.Operation + "完成，请留意提示" : report.Operation + "完成";
        var w = Shell(owner, title, 780, 660);
        var body = Frame(w, "知笺  /  任务报告", title, "逐项查看处理结果，点击“复制报告”可复制完整详情。", out var actions);
        var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var pair in new[] { ("成功", report.SuccessCount), ("失败", report.FailedCount), ("跳过", skipped) })
        {
            var tile = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
            var number = Label(pair.Item2.ToString(), 30); number.FontWeight = FontWeights.SemiBold; number.Foreground = Brush(w, "AccentTextBrush");
            tile.Children.Add(number); tile.Children.Add(Label(pair.Item1, 12));
            metrics.Children.Add(new Border { Child = tile, Background = Brush(w, "HoverBrush"), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8) });
        }
        body.Children.Add(metrics);
        if (report.OutputPath != null) Card(w, body, "文件已保存至", report.OutputPath);
        // Virtualize long batch reports rather than creating hundreds of editable controls.
        var list = new ListBox { Height = 280, ItemContainerStyle = (Style)w.FindResource(typeof(ListBoxItem)), ItemsSource = report.Items.Select(i => new ReportRow(i.Status == "SUCCESS" ? "成功" : i.Status == "FAILED" ? "失败" : "跳过", i.Source, i.Message ?? "")).ToArray() };
        var template = new DataTemplate(typeof(ReportRow));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        foreach (var property in new[] { "Status", "Source", "Message" })
        {
            var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(property)); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 2));
            if (property == "Status") text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            panel.AppendChild(text);
        }
        template.VisualTree = panel; list.ItemTemplate = template; body.Children.Add(list);
        Action(w, actions, "复制报告", () => { try { Clipboard.SetText(report + (report.OutputPath == null ? "" : "\n输出目录：" + report.OutputPath)); } catch { Text(w, "复制未完成", "剪贴板暂时不可用，请稍后重试。"); } });
        Action(w, actions, "完成", () => w.Close(), true, true); w.ShowDialog();
    }
    private sealed record ReportRow(string Status, string Source, string Message);
    public static void Help(Window owner, string text)
    {
        var w = Shell(owner, "知笺 · 使用指南", 820, 720);
        var body = Frame(w, "知笺  /  使用指南", "把记录，变成自己的知识", "从第一篇笔记开始，了解整理、连接与保护笔记的方法。", out var actions);
        Card(w, body, "常用快捷键", "Ctrl + N  新建笔记     ·     Ctrl + S  保存\nCtrl + K  搜索笔记     ·     F1  使用帮助");
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 14), ToolTip = "搜索帮助，例如：备份、标签、双链" };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索帮助");
        body.Children.Add(Label("搜索帮助", 12)); body.Children.Add(search);
        var sections = new StackPanel(); body.Children.Add(sections);
        var blocks = text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(b => !b.StartsWith("快捷键\n")).ToArray();
        void Filter()
        {
            sections.Children.Clear();
            foreach (var block in blocks.Where(b => b.Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            { var split = block.IndexOf('\n'); Card(w, sections, split < 0 ? block : block[..split], split < 0 ? "" : block[(split + 1)..]); }
            if (sections.Children.Count == 0) sections.Children.Add(Label("没有找到相关内容，请试试其他关键词。"));
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        Action(w, actions, "开始记录", () => w.Close(), true, true); w.ShowDialog();
    }
    public static NoteRecord? PickNote(Window owner, NoteRecord[] notes)
    {
        var w = Shell(owner, "插入双链", 620, 600);
        var body = Frame(w, "知笺  /  笔记关联", "连接另一篇笔记", "搜索标题并选择目标，建立可追踪的知识连接。", out var actions);
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 12) }; body.Children.Add(search);
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索目标笔记");
        var list = new ListBox { Height = 260, DisplayMemberPath = "Label", ItemContainerStyle = (Style)owner.FindResource(typeof(ListBoxItem)) }; body.Children.Add(list);
        var empty = Label("没有匹配的笔记，请更换关键词或先创建另一篇笔记。", 12); body.Children.Add(empty);
        NoteRecord? selected = null;
        Action(w, actions, "取消", () => w.Close(), false, true);
        void Accept() { if (list.SelectedItem is NoteChoice choice) { selected = choice.Note; w.Close(); } }
        var insert = Action(w, actions, "插入链接", Accept, true); insert.IsEnabled = false;
        list.SelectionChanged += (_, _) => insert.IsEnabled = list.SelectedItem != null;
        void Filter() { var items = notes.Where(n => Rules.Fold(n.Title!).Contains(Rules.Fold(search.Text))).Select(n => new NoteChoice(n)).ToArray(); list.ItemsSource = items; empty.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
        search.TextChanged += (_, _) => Filter(); list.MouseDoubleClick += (_, _) => Accept(); Filter();
        w.Loaded += (_, _) => search.Focus(); w.ShowDialog(); return selected;
    }
    private sealed record NoteChoice(NoteRecord Note) { public string Label => $"{Note.Title}    · {Note.ShortId}"; }
    public static string ClosingChoice(Window owner)
    {
        var w = Shell(owner, "保存当前编辑", 620, 410);
        var body = Frame(w, "知笺  /  离开之前", "还有尚未保存的修改", "选择一种保存方式，让这次记录完整留下。", out var actions);
        Card(w, body, "两种保存方式", "保存并关闭：将修改写入当前工作区。\n另存并关闭：将当前正文保存为独立 Markdown 文件。");
        string choice = "cancel";
        Action(w, actions, "继续编辑", () => w.Close(), false, true);
        Action(w, actions, "另存并关闭", () => { choice = "saveas"; w.Close(); });
        Action(w, actions, "保存并关闭", () => { choice = "save"; w.Close(); }, true);
        w.ShowDialog(); return choice;
    }
}
