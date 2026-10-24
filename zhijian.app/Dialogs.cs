using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zhijian.Core;

namespace zhijian.app;

internal static class Dialogs
{
    private static Window Shell(Window owner, string title, double width, double height) => new()
    {
        Owner = owner, Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = (Brush)owner.FindResource("PanelBrush"), Foreground = (Brush)owner.FindResource("TextBrush"),
        FontFamily = owner.FontFamily, FontSize = 14, Resources = owner.Resources, ShowInTaskbar = false
    };
    public static void Text(Window owner, string title, string text)
    {
        var w = Shell(owner, title, 740, 580); var panel = new DockPanel { Margin = new Thickness(24) };
        var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0), IsCancel = true };
        close.Click += (_, _) => w.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Top });
        w.Content = panel; w.ShowDialog();
    }
    public static NoteRecord? PickNote(Window owner, NoteRecord[] notes)
    {
        var w = Shell(owner, "插入双链 · 选择目标笔记", 560, 580); var panel = new DockPanel { Margin = new Thickness(24) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var insert = new Button { Content = "插入链接", IsDefault = true }; var cancel = new Button { Content = "取消", IsCancel = true }; buttons.Children.Add(insert); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var list = new ListBox { DisplayMemberPath = "Label", ItemContainerStyle = (Style)owner.FindResource(typeof(ListBoxItem)), HorizontalContentAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(list);
        void Filter() => list.ItemsSource = notes.Where(n => Rules.Fold(n.Title!).Contains(Rules.Fold(search.Text))).Select(n => new { Label = $"{n.Title}    · {n.ShortId}", Note = n }).ToArray();
        NoteRecord? selected = null;
        void Accept() { if (list.SelectedItem == null) return; selected = (NoteRecord)list.SelectedItem.GetType().GetProperty("Note")!.GetValue(list.SelectedItem)!; w.DialogResult = true; }
        search.TextChanged += (_, _) => Filter(); insert.Click += (_, _) => Accept(); list.MouseDoubleClick += (_, _) => Accept();
        Filter(); w.Content = panel; w.ShowDialog(); return selected;
    }
    public static string ClosingChoice(Window owner)
    {
        var w = Shell(owner, "当前笔记尚未保存", 510, 230); w.ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(new TextBlock { Text = "请保存当前编辑后关闭，或另存一份 Markdown。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 25) });
        var row = new WrapPanel(); string choice = "cancel";
        foreach (var (label, value) in new[] { ("保存并关闭", "save"), ("另存并关闭", "saveas"), ("取消", "cancel") })
        { var b = new Button { Content = label }; b.Click += (_, _) => { choice = value; w.Close(); }; row.Children.Add(b); }
        panel.Children.Add(row); w.Content = panel; w.ShowDialog(); return choice;
    }
}
