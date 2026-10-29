namespace Zhijian.Core;

public sealed record ImportPreviewItem(string Path, bool CanImport, int MappedLinks, string[] Warnings, string? Error);
public static class ImportPreview
{
    public static ImportPreviewItem[] Analyze(string[] paths, CancellationToken cancel = default)
    {
        if (paths.Length > 500) throw new InvalidDataException("每次最多导入500个文件，请分批选择。");
        var valid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unique = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicateIds = unique.Select(Path.GetFileNameWithoutExtension).Where(n => Rules.IsId(n?.ToLowerInvariant())).GroupBy(n => n!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in unique)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只支持.md文件。");
                Rules.Title(Path.GetFileNameWithoutExtension(path));
                if (duplicateIds.Contains(Path.GetFileNameWithoutExtension(path))) throw new InvalidDataException("同批次旧ID重复，无法确定链接目标。");
                if (new FileInfo(path).Length > Rules.MaxBodyBytes) throw new InvalidDataException("超过1 MiB，请拆分文件。");
                valid[path] = Rules.Utf8.GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { errors[path] = ex.Message; }
        }
        var ids = valid.Keys.Select(Path.GetFileNameWithoutExtension).Where(n => Rules.IsId(n?.ToLowerInvariant())).Select(n => n!.ToLowerInvariant()).ToHashSet();
        return unique.Select(path =>
        {
            cancel.ThrowIfCancellationRequested();
            if (errors.TryGetValue(path, out var error)) return new ImportPreviewItem(path, false, 0, [], error);
            var warnings = new List<string>(); int mapped = 0;
            foreach (var link in MarkdownLinks.Links(valid[path]))
            {
                string url = link.Url ?? ""; var id = MarkdownLinks.Target(url);
                if (id != null) { if (ids.Contains(id)) mapped++; else warnings.Add($"未选择note链接目标：{id}。导入时可能失效或转为普通文字。"); continue; }
                if (Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
                try
                {
                    var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(url)));
                    if (valid.ContainsKey(target)) { mapped++; continue; }
                }
                catch (ArgumentException) { }
                warnings.Add($"缺少同批目标：{url}。继续导入会转为普通文字；请返回并同时选中目标文件。");
            }
            return new ImportPreviewItem(path, true, mapped, warnings.Distinct().ToArray(), null);
        }).ToArray();
    }
}
