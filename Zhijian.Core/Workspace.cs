using System.Text;

namespace Zhijian.Core;

public sealed partial class Workspace : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, NoteRecord> records = new();
    private readonly FileStream workspaceLock;
    private NoteIndex? index;
    private bool disposed;
    private long generation;
    private readonly Action<string>? fault;
    public string Path { get; }
    public WorkspaceInfo Info { get; }
    public Preferences Settings { get; private set; }
    public bool IndexReady { get; private set; }
    public List<string> Warnings { get; } = [];
    public int Count { get { lock (gate) return records.Values.Count(n => n.State == "ACTIVE"); } }
    public int TrashCount { get { lock (gate) return records.Values.Count(n => n.State == "TRASH"); } }
    private string At(string relative) => System.IO.Path.Combine(Path, relative);
    private string RecordPath(string id) => At($"records/{id}.json");
    private string BodyPath(NoteRecord n) => At($"bodies/{n.Id}/{n.BodyRev}.md");

    public Workspace(string path, bool create = false, IProgress<string>? progress = null, Action<string>? faultInjector = null)
    {
        Path = Disk.Root(path); fault = faultInjector;
        if (create)
        {
            if (Directory.Exists(Path) && Directory.EnumerateFileSystemEntries(Path).Any()) throw new IOException("创建工作区需要空目录，请新建一个文件夹。");
            Directory.CreateDirectory(Path);
        }
        if (!Directory.Exists(Path)) throw new DirectoryNotFoundException("工作区目录不存在，请重新选择。");
        if (File.Exists(At("restore-incomplete"))) throw new IOException("该目录的恢复尚未完成，请选择其他空目录重新恢复。");
        if (!create && !File.Exists(At("workspace.json"))) throw new InvalidDataException("此目录不是知笺工作区，请选择含 workspace.json 的目录。");
        Directory.CreateDirectory(At(".state")); Disk.NoReparse(At(".state"));
        try { workspaceLock = new FileStream(At(".state/workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("工作区正在被其他窗口占用，或目录不可写。请关闭占用窗口后重试。", ex); }
        try
        {
            foreach (var dir in new[] { "records", "bodies", ".index", ".tmp" }) { Disk.NoReparse(At(dir)); Directory.CreateDirectory(At(dir)); }
            if (create)
            {
                Disk.Atomic(At("workspace.json"), Disk.Serialize(new WorkspaceInfo(1, Guid.NewGuid().ToString())));
                Disk.Atomic(At("settings.json"), Disk.Serialize(new Preferences()));
            }
            Info = Disk.Read<WorkspaceInfo>(At("workspace.json"));
            if (Info.SchemaVersion != 1 || !Rules.IsId(Info.WorkspaceId)) throw new InvalidDataException("工作区格式版本不受支持，请使用兼容版本。");
            Settings = Disk.Read<Preferences>(At("settings.json"));
            if (Settings.SchemaVersion != 1 || Settings.Theme is not ("light" or "dark")) throw new InvalidDataException("设置格式不受支持。");
            foreach (var file in Directory.EnumerateFiles(At("records"), "*.json"))
            {
                try
                {
                    Disk.NoReparse(file); var n = Disk.Read<NoteRecord>(file); Rules.Validate(n);
                    if (System.IO.Path.GetFileNameWithoutExtension(file) != n.Id) throw new InvalidDataException("记录 ID 与文件名不符。");
                    records.Add(n.Id, n);
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException)
                { Warnings.Add($"已隔离 {System.IO.Path.GetFileName(file)}：{ex.Message}。原文件保留，可从备份恢复。"); }
            }
            try
            {
                index = new NoteIndex(At(".index/index.db"));
                IndexReady = Warnings.Count == 0 && !File.Exists(At(".state/dirty")) && index.Valid(Digest());
                if (!IndexReady && Warnings.Count == 0) RebuildIndex(progress);
            }
            catch (Exception ex)
            {
                index?.Dispose(); index = null; IndexReady = false;
                try { RebuildIndex(progress); }
                catch { Warnings.Add($"索引不可用：{ex.Message}。可点击重建索引。"); }
            }
        }
        catch { index?.Dispose(); workspaceLock.Dispose(); throw; }
    }
    private void Ensure()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Directory.Exists(Path) || !File.Exists(At("workspace.json"))) throw new IOException("工作区已移除，当前编辑仍在内存，请恢复目录或另存正文。");
        var current = Disk.Read<WorkspaceInfo>(At("workspace.json"));
        if (current != Info) throw new IOException("工作区身份已变化，请关闭后重新打开。");
    }
    private string Digest() => Disk.Hash(Encoding.UTF8.GetBytes(string.Join("\n", Directory.EnumerateFiles(At("records"), "*.json")
        .Order(StringComparer.Ordinal).Select(p => System.IO.Path.GetFileName(p) + ":" + Disk.Hash(File.ReadAllBytes(p))))));
    private string ReadBody(NoteRecord n)
    {
        if (n.State == "PURGED") throw new InvalidOperationException("笔记已永久删除。");
        var p = BodyPath(n); Disk.NoReparse(p); var bytes = File.ReadAllBytes(p);
        if (bytes.Length != n.BodyBytes || Disk.Hash(bytes) != n.BodySha256) throw new InvalidDataException($"《{n.Title}》正文校验失败，已阻止编辑。请从备份恢复。");
        return Rules.Utf8.GetString(bytes);
    }
    public NoteContent Get(string id) { lock (gate) { Ensure(); var n = records[id]; return new(n, ReadBody(n)); } }
    public NoteRecord[] All(bool trash = false) { lock (gate) return records.Values.Where(n => n.State == (trash ? "TRASH" : "ACTIVE")).OrderByDescending(n => n.UpdatedAt).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray(); }
    public string[] AllTags() { lock (gate) return records.Values.Where(n => n.State == "ACTIVE").SelectMany(n => n.Tags!).DistinctBy(Rules.Fold).Order().ToArray(); }
    public SearchPage Search(string query, string[] tags, bool trash = false, int offset = 0, long? expectedGeneration = null, bool favoritesOnly = false)
    {
        lock (gate)
        {
            Ensure(); tags = Rules.Tags(tags);
            if (expectedGeneration.HasValue && expectedGeneration != generation) throw new InvalidOperationException("列表已更新，请刷新后继续翻页。");
            if (Rules.Length(query) > 100) throw new InvalidDataException("搜索词最多 100 个字符。");
            NoteRecord[] items;
            if (query.Length > 0 && !trash)
            {
                if (!IndexReady || index == null) throw new InvalidOperationException("索引尚未就绪，请先重建索引。");
                items = index.Search(query, tags, 0, int.MaxValue).Select(id => records[id])
                    .Where(n => !favoritesOnly || n.Favorite).OrderByDescending(n => n.Pinned)
                    .ThenByDescending(n => n.UpdatedAt).ThenBy(n => n.Id, StringComparer.Ordinal).Skip(offset).Take(51).ToArray();
            }
            else
            {
                items = All(trash).Where(n => tags.All(t => n.Tags!.Any(nt => Rules.Fold(nt) == Rules.Fold(t))))
                    .Where(n => query.Length == 0 || Rules.Fold(n.Title!).Contains(Rules.Fold(query), StringComparison.Ordinal))
                    .Where(n => !favoritesOnly || n.Favorite).OrderByDescending(n => !trash && n.Pinned)
                    .ThenByDescending(n => n.UpdatedAt).ThenBy(n => n.Id, StringComparer.Ordinal)
                    .Skip(offset).Take(51).ToArray();
            }
            var page = items.Take(50).ToArray();
            if (query.Length > 0 && !trash)
                page = page.Select(n => n with { Snippet = Snippet(ReadBody(n), query) }).ToArray();
            return new(page, items.Length > 50, generation);
        }
    }
    private static string Snippet(string body, string query)
    {
        var at = Rules.Fold(body).IndexOf(Rules.Fold(query), StringComparison.Ordinal);
        var start = Math.Max(0, at - 24);
        if (start > 0 && char.IsLowSurrogate(body[start])) start--;
        var text = string.Concat(body[start..].EnumerateRunes().Take(120).Select(r => r.ToString())).Replace('\r', ' ').Replace('\n', ' ');
        return (start > 0 ? "…" : "") + text;
    }
    public NoteRecord[] Backlinks(string id)
    {
        lock (gate) return IndexReady && index != null ? index.Backlinks(id).Where(records.ContainsKey).Select(x => records[x]).OrderByDescending(n => n.UpdatedAt).ToArray() : [];
    }
    private void Dirty() => Disk.Atomic(At(".state/dirty"), Encoding.UTF8.GetBytes("pending"));
    public NoteRecord Create(string title, string body = "", IEnumerable<string>? tags = null, string? id = null)
    {
        lock (gate)
        {
            Ensure(); id ??= Guid.NewGuid().ToString(); if (!Rules.IsId(id) || records.ContainsKey(id)) throw new InvalidOperationException("笔记 ID 冲突。");
            var n = new NoteRecord { Id = id, Title = Rules.Title(title), Tags = Rules.Tags(tags ?? []) };
            return Commit(n, body);
        }
    }
    public NoteRecord Save(string id, long baseRevision, string title, string body, IEnumerable<string> tags)
    {
        lock (gate)
        {
            Ensure(); var old = Current(id, baseRevision);
            if (old.State != "ACTIVE") throw new InvalidOperationException("回收站笔记不可编辑，请先恢复。");
            return Commit(old with { Revision = old.Revision + 1, Title = Rules.Title(title), Tags = Rules.Tags(tags), UpdatedAt = DateTimeOffset.UtcNow }, body);
        }
    }
    private NoteRecord Current(string id, long revision)
    {
        if (!records.TryGetValue(id, out var n) || n.Revision != revision) throw new InvalidOperationException("笔记版本已变化，已保留当前编辑，请另存或重新打开。");
        var onDisk = Disk.Read<NoteRecord>(RecordPath(id));
        if (onDisk != n && !Disk.Serialize(onDisk).SequenceEqual(Disk.Serialize(n))) throw new InvalidOperationException("笔记被外部程序修改，请另存当前编辑后重新打开工作区。");
        return n;
    }
    private NoteRecord Commit(NoteRecord n, string? body)
    {
        var previousBodyRev = records.TryGetValue(n.Id, out var previous) ? previous.BodyRev : null;
        byte[]? bytes = body == null ? null : Rules.Body(body);
        if (bytes != null && n.BodyRev != null && n.BodySha256 == Disk.Hash(bytes)) ReadBody(n);
        Dirty(); fault?.Invoke("dirty");
        if (bytes != null && (n.BodySha256 != Disk.Hash(bytes) || n.BodyRev == null))
        {
            var dir = At($"bodies/{n.Id}"); Disk.NoReparse(dir); Directory.CreateDirectory(dir);
            long rev = n.Revision;
            while (File.Exists(At($"bodies/{n.Id}/{rev}.md"))) rev++;
            n = n with { BodyRev = rev, BodyBytes = bytes.Length, BodySha256 = Disk.Hash(bytes) };
            Disk.Durable(BodyPath(n), bytes); fault?.Invoke("body");
        }
        Rules.Validate(n); Disk.Atomic(RecordPath(n.Id), Disk.Serialize(n));
        records[n.Id] = n; generation++;
        fault?.Invoke("record");
        try { if (index != null && IndexReady) index.Update(n, n.State == "ACTIVE" ? body ?? ReadBody(n) : ""); }
        catch { IndexReady = false; }
        // The workspace gate also protects backups. Keep the current and previous body;
        // orphan versions are never adopted as authority and can be reclaimed after commit.
        if (n.State != "PURGED")
        {
            try
            {
                long? protectedRev = File.Exists(RecordPath(n.Id) + ".previous") ? Disk.Read<NoteRecord>(RecordPath(n.Id) + ".previous").BodyRev : null;
                foreach (var file in Directory.EnumerateFiles(At($"bodies/{n.Id}"), "*.md"))
                    if (long.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), out var rev) && rev != n.BodyRev && rev != previousBodyRev && rev != protectedRev)
                    { Disk.NoReparse(file); File.Delete(file); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return n;
    }
    public NoteRecord SetTrash(string id, long revision, bool trash)
    {
        lock (gate)
        {
            Ensure(); var n = Current(id, revision);
            if (n.State != (trash ? "ACTIVE" : "TRASH")) throw new InvalidOperationException("笔记状态已变化。");
            ReadBody(n);
            return Commit(n with { Revision = n.Revision + 1, State = trash ? "TRASH" : "ACTIVE", DeletedAt = trash ? DateTimeOffset.UtcNow : null, UpdatedAt = DateTimeOffset.UtcNow }, null);
        }
    }
    public void Purge(string id, long revision)
    {
        lock (gate)
        {
            Ensure(); var n = Current(id, revision); if (n.State != "TRASH") throw new InvalidOperationException("只能永久删除回收站中的笔记。");
            Commit(new NoteRecord { Id = id, Revision = n.Revision + 1, State = "PURGED", CreatedAt = null }, null);
            var dir = At($"bodies/{id}");
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
                    try { Disk.NoReparse(file); File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public void SetTheme(string theme)
    {
        lock (gate)
        {
            Ensure(); if (theme is not ("light" or "dark")) throw new ArgumentException("无效主题。");
            var next = Settings with { Theme = theme }; Disk.Atomic(At("settings.json"), Disk.Serialize(next)); Settings = next;
        }
    }
    public void RebuildIndex(IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        lock (gate)
        {
            Ensure(); IndexReady = false; Dirty();
            var temp = At($".index/rebuild-{Guid.NewGuid():N}.db");
            try
            {
                if (Warnings.Any(w => w.StartsWith("已隔离"))) throw new InvalidDataException("存在损坏的记录，原文件已保留。请先从备份恢复工作区。");
                using (var next = new NoteIndex(temp))
                {
                    next.Populate(records.Values.Where(n => n.State == "ACTIVE").Select(n => new NoteContent(n, ReadBody(n))), cancel, progress);
                    next.Stamp(Digest());
                }
                index?.Dispose(); index = null;
                File.Move(temp, At(".index/index.db"), true); index = new NoteIndex(At(".index/index.db"));
                IndexReady = true; generation++; File.Delete(At(".state/dirty"));
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            try { if (IndexReady && index != null) { index.Stamp(Digest()); File.Delete(At(".state/dirty")); } } catch { }
            index?.Dispose(); workspaceLock.Dispose(); disposed = true;
        }
    }
}
