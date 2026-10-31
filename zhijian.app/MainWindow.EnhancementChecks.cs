using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zhijian.Core;
namespace zhijian.app;

public partial class MainWindow
{
    private async Task CheckEnhancements(string output, Action<bool, string> check)
    {
        var id = current!.Id;
        await ToggleMark(true); await ToggleMark(false);
        check(current!.Pinned && current.Favorite && PinMenu.IsChecked && FavoriteMenu.IsChecked, "置顶收藏菜单与持久记录同步");
        check(visibleNotes[0].Id == id, "置顶笔记排在列表首位");
        await SetView(false, true);
        check(visibleNotes.Count == 1 && visibleNotes[0].Id == id && ListHeading.Text == "我的收藏", "收藏入口筛选笔记");
        await ToggleMark(false); check(visibleNotes.Count == 0, "取消收藏即时退出收藏列表");
        await SetView(false); await OpenNote(id); await ToggleMark(false);
        await ChangeTrash(true); await SetView(true);
        check(!PinMenu.IsEnabled && !FavoriteMenu.IsEnabled, "回收站禁止置顶收藏操作");
        await ChangeTrash(false); await SetView(false); await OpenNote(id);
        check(current!.Pinned && current.Favorite, "从回收站恢复保留置顶收藏");
        workspace!.SetLayout(310, 0.6); RestoreLayout(); UpdateLayout();
        check(WorkspaceColumns.ColumnDefinitions[1].Width.Value == 310 && Math.Abs(splitRatio - 0.6) < 0.001, "分栏配置恢复");
        SetEditorLayout("preview"); SetEditorLayout("split");
        check(Math.Abs(EditorColumns.ColumnDefinitions[0].Width.Value - 0.6) < 0.001, "切换视图保留分栏比例");
        Divider_DragCompleted(EditorDivider, new DragCompletedEventArgs(0, 0, false));
        check(Math.Abs(workspace.Settings.ListWidth - 310) < 1, "拖动结束保存列表宽度");
        var bytes = File.ReadAllBytes(Path.Combine(output, "fixture-moved.png"));
        var file = Path.Combine(output, "drag-image.png"); File.WriteAllBytes(file, bytes);
        var data = new DataObject(DataFormats.FileDrop, new[] { file, "ignore.txt" });
        check(DroppedImages(data).SequenceEqual(new[] { file }), "拖入只接受PNG或JPEG");
        BodyBox.Select(BodyBox.Text.Length, 0); await InsertDroppedImages(DroppedImages(data));
        var image = MarkdownLinks.Parse(BodyBox.Text).Descendants<LinkInline>().Last(l => l.IsImage);
        ResizeImage(image.Span.Start, image.Span.End, 240);
        check(BodyBox.Text.Contains("\"width=240\""), "图片尺寸写入Markdown标题字段");
        var doc = SafePreview.Render(BodyBox.Text, Brushes.Black, Brushes.Green, _ => { }, workspace.ReadImage);
        var pictures = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineUIContainer>()).Select(i => i.Child).OfType<System.Windows.Controls.Image>().ToArray();
        check(pictures.Any(p => p.MaxWidth == 240 && p.Cursor == System.Windows.Input.Cursors.Hand), "预览按尺寸渲染且支持点击");
        BodyBox.Undo(); check(!BodyBox.Text.Contains("\"width=240\""), "图片尺寸单次撤销");
        var paste = new DataObject(DataFormats.Bitmap, ImageCodec.Decode(bytes));
        BodyBox.RaiseEvent(new DataObjectPastingEventArgs(paste, false, DataFormats.Bitmap));
        for (var i = 0; busy && i < 100; i++) await Task.Delay(20);
        await Task.Delay(100); await SaveCurrent();
        check(MarkdownLinks.Parse(BodyBox.Text).Descendants<LinkInline>().Count(l => l.IsImage) >= 3, "直接粘贴位图插入并保存");
        workspace.SetLayout(270, 0.5); RestoreLayout();
        RenderPreview();
    }
}
