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

        笔记关联
        点击“插入双链”选择目标。反向引用列出引用当前笔记的来源，双击打开。重命名不改变链接；删除后暂时失效，恢复后继续有效。

        保存
        停止编辑 2 秒自动保存，或按 Ctrl+S。以底部文字状态为准。保存失败时内容仍在编辑器，可修复后重试或“另存正文”。

        标签与搜索
        标签用逗号分隔，每篇最多 20 个，每个最多 20 字符。左侧可选择多个标签，按交集筛选。搜索按标题及正文的字面子串匹配，英文字母忽略大小写；回收站只搜索标题。每次显示 50 篇，可加载更多。

        导入与导出
        支持 UTF-8 .md，每篇最多 1 MiB，每批最多 500 个。导入生成新 ID，批内可识别的链接会重新映射。导出文件名使用 UUID 防止重名；标签与设置请通过完整备份迁移。每次导出创建独立目录并生成 report.json。

        备份恢复
        完整备份包含有效笔记、回收站和设置。恢复会校验完整性，只能恢复到新的空目录。失败或取消的备份带 .partial 后缀；失败的恢复目录包含 restore-incomplete，不能直接打开，可另选空目录重新恢复。

        隐私与安全
        笔记保存在本机，无账户或在线 API。预览不执行 HTML 或脚本，不加载远程图片。仅在手动点击并确认后用默认浏览器打开 http/https 链接。

        V1 范围
        Windows 11 x64，纯文字 Markdown；不含同步、移动端、多人协作、插件市场、PDF/Word 导出。
        """;
    private void Help_Click(object sender, RoutedEventArgs e) => Dialogs.Text(this, "知笺 · 使用帮助", HelpText);
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
        if (busy) { MessageBox.Show(this, "请等待当前任务完成，或取消任务后关闭窗口。"); return; }
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
