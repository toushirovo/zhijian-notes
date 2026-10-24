using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zhijian.Core;

public sealed record NoteRecord
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public long Revision { get; init; } = 1;
    public string? Title { get; init; }
    public string[]? Tags { get; init; }
    public DateTimeOffset? CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string State { get; init; } = "ACTIVE";
    public DateTimeOffset? DeletedAt { get; init; }
    public long? BodyRev { get; init; }
    public string? BodySha256 { get; init; }
    public int? BodyBytes { get; init; }
    [JsonIgnore] public string TagText => string.Join(" · ", Tags ?? []);
    [JsonIgnore] public string ShortId => Id[..8];
    [JsonIgnore] public string Snippet { get; init; } = "";
    [JsonIgnore] public string DisplayTime => (State == "TRASH" ? DeletedAt ?? UpdatedAt : UpdatedAt).ToLocalTime().ToString("MM-dd HH:mm");
}

public sealed record WorkspaceInfo(int SchemaVersion, string WorkspaceId);
public sealed record Preferences(int SchemaVersion = 1, string Theme = "light");
public sealed record NoteContent(NoteRecord Record, string Body);
public sealed record SearchPage(IReadOnlyList<NoteRecord> Items, bool HasMore, long Generation);
public sealed record BatchItem(string Source, string Status, string? NoteId = null, string? Message = null);
public sealed record BatchReport(string Operation, List<BatchItem> Items, string? OutputPath = null)
{
    public int SuccessCount => Items.Count(x => x.Status == "SUCCESS");
    public int FailedCount => Items.Count(x => x.Status == "FAILED");
    public override string ToString() => $"{Operation}：成功 {SuccessCount}，失败 {FailedCount}\n" +
        string.Join("\n", Items.Select(x => $"[{x.Status}] {x.Source} {x.Message}"));
}
public sealed record BackupFile(string Path, long Bytes, string Sha256);
public sealed record BackupManifest(int FormatVersion, string SourceWorkspaceId, DateTimeOffset CreatedAt,
    int NoteCount, int TrashCount, List<BackupFile> Files);

public static class Rules
{
    public const int MaxBodyBytes = 1024 * 1024;
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static string Fold(string s) => string.Create(s.Length, s, (span, source) =>
    { for (int i = 0; i < span.Length; i++) span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i]; });
    public static int Length(string s) => s.EnumerateRunes().Count();
    public static bool IsId(string? s) => s != null && Guid.TryParseExact(s, "D", out var id) && id.ToString() == s;
    public static string Title(string title)
    {
        title = title.Trim();
        if (Length(title) is < 1 or > 100) throw new InvalidDataException("标题须为 1～100 个字符，请修改标题后重试。");
        return title;
    }
    public static string[] Tags(IEnumerable<string> tags)
    {
        var result = tags.Select(x => x.Trim()).Where(x => x.Length > 0).DistinctBy(Fold).ToArray();
        if (result.Length > 20 || result.Any(x => Length(x) > 20))
            throw new InvalidDataException("每篇最多 20 个标签，每个标签最多 20 个字符，请缩短后重试。");
        return result;
    }
    public static byte[] Body(string body)
    {
        var bytes = Utf8.GetBytes(body);
        if (bytes.Length > MaxBodyBytes) throw new InvalidDataException("正文超过 1 MiB，请拆分笔记或另存正文。");
        return bytes;
    }
    public static void Validate(NoteRecord n)
    {
        if (n.SchemaVersion != 1 || !IsId(n.Id) || n.Revision < 1 || n.UpdatedAt == default)
            throw new InvalidDataException("笔记格式或版本不受支持。");
        if (n.State == "PURGED") return;
        if (n.State is not ("ACTIVE" or "TRASH") || n.BodyRev is null or < 1 || n.BodyBytes is null or < 0 or > MaxBodyBytes ||
            n.BodySha256?.Length != 64 || n.CreatedAt == null || n.Title == null || n.Tags == null ||
            (n.State == "TRASH") != (n.DeletedAt != null)) throw new InvalidDataException("笔记元数据不完整。");
        if (Title(n.Title) != n.Title || !Tags(n.Tags).SequenceEqual(n.Tags)) throw new InvalidDataException("标题或标签格式错误。");
    }
}
