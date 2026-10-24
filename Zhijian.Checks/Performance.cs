using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Zhijian.Core;

internal static class Performance
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); var path = Path.Combine(output, "workspace");
        using (var empty = new Workspace(path, true)) { }
        var ids = Enumerable.Range(0, 5000).Select(i => new Guid(i, 0, 0, new byte[8]).ToString()).ToArray();
        long total = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            var links = string.Join("\n", Enumerable.Range(1, 4).Select(j => $"[相关笔记](note:{ids[(i + j) % ids.Length]})"));
            var body = $"# 课程笔记 {i}\n\n数据库事务 SQLite 复习关键词-{i:D4}\n" + links + "\n" + new string('a', 19500);
            var bytes = Rules.Utf8.GetBytes(body); total += bytes.Length;
            var n = new NoteRecord { Id = ids[i], Title = "课程笔记 " + i, Tags = ["课程", i % 2 == 0 ? "计算机" : "阅读"], BodyRev = 1, BodyBytes = bytes.Length, BodySha256 = Disk.Hash(bytes) };
            Directory.CreateDirectory(Path.Combine(path, "bodies", n.Id));
            File.WriteAllBytes(Path.Combine(path, "bodies", n.Id, "1.md"), bytes);
            File.WriteAllBytes(Path.Combine(path, "records", n.Id + ".json"), Disk.Serialize(n));
        }
        Console.WriteLine($"Fixture: 5000 notes, {total / 1048576.0:F2} MiB, 20000 links");
        var sw = Stopwatch.StartNew(); var workspace = new Workspace(path); var rebuildMs = sw.Elapsed.TotalMilliseconds;
        var queryMs = new List<double>(); var getMs = new List<double>(); var saveMs = new List<double>(); var openMs = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            sw.Restart(); var result = workspace.Search($"关键词-{(i * 163):D4}", ["课程"]); queryMs.Add(sw.Elapsed.TotalMilliseconds);
            if (result.Items.Count != 1) throw new Exception("搜索结果错误");
            sw.Restart(); var note = workspace.Get(ids[i]); getMs.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart(); workspace.Save(note.Record.Id, note.Record.Revision, note.Record.Title!, note.Body + "\n保存测试", note.Record.Tags!); saveMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        workspace.Dispose();
        for (int i = 0; i < 30; i++)
        {
            sw.Restart(); workspace = new Workspace(path); openMs.Add(sw.Elapsed.TotalMilliseconds); workspace.Dispose();
        }
        double P95(List<double> v) => v.Order().ElementAt(28);
        var data = new { notes = 5000, bodyBytes = total, links = 20000, repetitions = 30, rebuildMs,
            openP95Ms = P95(openMs), searchP95Ms = P95(queryMs), readP95Ms = P95(getMs), saveP95Ms = P95(saveMs),
            openMs, queryMs, getMs, saveMs, measurement = "Service layer only; warm OS cache, no WPF rendering/input delay. Rebuild includes initial workspace open. Hardware is current development PC." };
        File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rebuild/open {rebuildMs:F1} ms; P95 open {P95(openMs):F1}, search {P95(queryMs):F1}, read {P95(getMs):F1}, save {P95(saveMs):F1} ms");
    }
}
