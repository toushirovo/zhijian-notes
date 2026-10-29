using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
namespace zhijian.app;
public partial class MainWindow
{
    private const string HelpText = """
        知笺 · 离线使用帮助

        开始
        新建工作区时选择空文件夹。工作区由应用管理，请勿运行时用其他编辑器修改内部文件，也不要放在正在同步的云盘目录。

        快捷键
        Ctrl+N：新建笔记
        Ctrl+S：保存
        Ctrl+K：搜索
        F1：帮助

        Markdown
        # 一级标题
        ## 二级标题
        **粗体** 和 *斜体*
        - 无序列表
        1. 有序列表
        > 引用
        `行内代码`
        使用三个反引号围住代码块。
        [链接文字](https://example.com)

        编辑工具栏与布局
        选中正文后点击B、I可加粗或斜体；标题、列表、引用、代码、网页链接按钮可插入对应语法，没有选区时插入可替换的占位文字。Ctrl+Z可撤销。网页链接需把示例地址替换为自己的网址。
        点击“仅编辑”“分屏”“仅预览”切换布局；当前窗口内保留选择，重新启动默认分屏。回收站笔记不能用工具栏修改。

        笔记关联
        点击“插入双链”选择目标。反向引用列出引用当前笔记的来源，双击打开。重命名不改变链接；删除后暂时失效，恢复后继续有效。

        保存
        停止编辑 2 秒自动保存，或按 Ctrl+S。以底部文字状态为准。保存失败时内容仍在编辑器，可修复后重试或“另存正文”。

        标签与搜索
        标签用逗号分隔，每篇最多 20 个，每个最多 20 字符。左侧可选择多个标签，按交集筛选。搜索按标题及正文的字面子串匹配，英文字母忽略大小写；回收站只搜索标题。每次显示 50 篇，可加载更多。

        导入与导出
        支持 UTF-8 .md，每篇最多 1 MiB，每批最多 500 个。选择文件后先检查编码、大小和批内链接；缺少目标时点击“返回选文件”同时补选目标，确认后再导入。导入生成新 ID，批内可识别的链接会重新映射。导出文件名使用 UUID 防止重名；标签与设置请通过完整备份迁移。每次导出创建独立目录并生成 report.json。

        备份恢复
        完整备份包含有效笔记、回收站和设置，创建前显示数量和保存位置供确认。恢复向导依次选择含manifest.json的备份、校验并查看备份时间与数量、选择独立空目录、确认恢复。恢复目录不能与备份或当前工作区互相包含。失败或取消的备份带.partial后缀；复制恢复开始后失败可能留下restore-incomplete标记，请另选空目录重试。

        隐私与安全
        笔记保存在本机，无账户或在线 API。预览不执行 HTML 或脚本，不加载远程图片。仅在手动点击并确认后用默认浏览器打开 http/https 链接。

        V1 范围
        Windows 11 x64，纯文字 Markdown；不含同步、移动端、多人协作、插件市场、PDF/Word 导出。
        """;
    private void Help_Click(object sender, RoutedEventArgs e) => Dialogs.Help(this, HelpText);
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.Key == Key.F1) { Help_Click(this, e); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S) { e.Handled = true; await SaveCurrent(true); }
        if (e.Key == Key.N) { e.Handled = true; await NewNote(); }
        if (e.Key == Key.K) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
    }
    private bool closePending;
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        // WaitAsync may complete synchronously. Always leave WPF's Closing stack
        // before showing dialogs or calling Close again.
        Dispatcher.BeginInvoke(new Action(async () => await CompleteCloseAsync()));
    }
    private async Task CompleteCloseAsync()
    {
        try
        {
        if (busy) { Dialogs.Text(this, "任务仍在进行", "请等待当前任务完成，或取消任务后关闭窗口。"); return; }
        if (dirty)
        {
            saveTimer.Stop(); var choice = Dialogs.ClosingChoice(this);
            if (choice == "cancel") { saveTimer.Start(); return; }
            if (choice == "save" && !await SaveCurrent(true)) return;
            if (choice == "saveas" && !SaveAs()) return;
        }
        IsEnabled = false;
        await saveGate.WaitAsync();
        try
        {
            saveTimer.Stop(); searchTimer.Stop(); previewTimer.Stop();
            workspace?.Dispose(); closing = true; Close();
        }
        finally { saveGate.Release(); }
        }
        catch (Exception ex) { closing = false; Error("关闭窗口", ex); }
        finally { closePending = false; if (!closing) IsEnabled = true; }
    }
}
