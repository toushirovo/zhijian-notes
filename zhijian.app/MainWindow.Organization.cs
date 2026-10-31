using System.Windows;
using System.Windows.Controls.Primitives;
namespace zhijian.app;

public partial class MainWindow
{
    private bool favoritesOnly;
    private double splitRatio = 0.5;
    private async void Favorites_Click(object sender, RoutedEventArgs e) => await SetView(false, true);
    private async void Pin_Click(object sender, RoutedEventArgs e) => await ToggleMark(true);
    private async void Favorite_Click(object sender, RoutedEventArgs e) => await ToggleMark(false);
    private void UpdateMarks()
    {
        PinMenu.IsEnabled = FavoriteMenu.IsEnabled = current?.State == "ACTIVE" && !busy;
        PinMenu.Header = current?.Pinned == true ? "取消置顶" : "置顶笔记";
        FavoriteMenu.Header = current?.Favorite == true ? "取消收藏" : "收藏笔记";
        PinMenu.IsChecked = current?.Pinned == true; FavoriteMenu.IsChecked = current?.Favorite == true;
    }
    private async Task ToggleMark(bool pin)
    {
        if (busy || current?.State != "ACTIVE" || !await SaveCurrent(true)) return;
        await saveGate.WaitAsync();
        try
        {
            var note = current!; var ws = workspace!;
            if (await RunJob("正在更新笔记标记…", async (_, _) =>
                current = await Task.Run(() => ws.SetMarks(note.Id, note.Revision, pin ? !note.Pinned : note.Pinned, pin ? note.Favorite : !note.Favorite))))
            { UpdateMarks(); await RefreshList(); SaveStatus.Text = "✓ 笔记标记已保存"; }
        }
        finally { saveGate.Release(); }
    }
    private void RestoreLayout()
    {
        var settings = workspace!.Settings;
        WorkspaceColumns.ColumnDefinitions[1].Width = new GridLength(double.IsFinite(settings.ListWidth) ? Math.Clamp(settings.ListWidth, 220, 380) : 270);
        splitRatio = double.IsFinite(settings.EditorRatio) ? Math.Clamp(settings.EditorRatio, 0.2, 0.8) : 0.5;
        SetEditorLayout(editorLayout);
    }
    private void Divider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled || busy || workspace == null) return;
        try
        {
            if (editorLayout == "split")
            {
                var left = EditorColumns.ColumnDefinitions[0].ActualWidth;
                var right = EditorColumns.ColumnDefinitions[2].ActualWidth;
                if (left + right > 0) splitRatio = Math.Clamp(left / (left + right), 0.2, 0.8);
            }
            workspace.SetLayout(Math.Clamp(WorkspaceColumns.ColumnDefinitions[1].ActualWidth, 220, 380), splitRatio);
        }
        catch (Exception ex) { Error("保存分栏布局", ex); }
    }
}
