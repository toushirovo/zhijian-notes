using Microsoft.Data.Sqlite;

namespace Zhijian.Core;

internal sealed class NoteIndex : IDisposable
{
    private readonly SqliteConnection db;
    public NoteIndex(string path)
    {
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 1 }.ToString());
        try
        {
        db.Open();
        Execute("PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; CREATE TABLE IF NOT EXISTS notes(id TEXT PRIMARY KEY, title TEXT NOT NULL, body TEXT NOT NULL, updated TEXT NOT NULL); CREATE TABLE IF NOT EXISTS tags(note_id TEXT REFERENCES notes(id) ON DELETE CASCADE, tag TEXT, PRIMARY KEY(note_id,tag)); CREATE INDEX IF NOT EXISTS ix_tags ON tags(tag,note_id); CREATE TABLE IF NOT EXISTS links(source TEXT REFERENCES notes(id) ON DELETE CASCADE,target TEXT,PRIMARY KEY(source,target)); CREATE INDEX IF NOT EXISTS ix_target ON links(target,source); CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT); PRAGMA user_version=1;");
        }
        catch { db.Dispose(); throw; }
    }
    private void Execute(string sql) { using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    public bool Valid(string digest)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA quick_check";
        if ((string?)cmd.ExecuteScalar() != "ok") return false;
        cmd.CommandText = "SELECT value FROM meta WHERE key='digest'";
        return (string?)cmd.ExecuteScalar() == digest;
    }
    public void Stamp(string digest)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT OR REPLACE INTO meta VALUES('digest',$d)";
        cmd.Parameters.AddWithValue("$d", digest); cmd.ExecuteNonQuery();
    }
    public void Update(NoteRecord n, string body)
    {
        using var tx = db.BeginTransaction();
        Update(n, body, tx); tx.Commit();
    }
    public void Populate(IEnumerable<NoteContent> notes, CancellationToken cancel, IProgress<string>? progress)
    {
        using var tx = db.BeginTransaction(); int count = 0;
        foreach (var note in notes)
        {
            cancel.ThrowIfCancellationRequested(); Update(note.Record, note.Body, tx);
            if (++count % 100 == 0) progress?.Report($"正在重建索引：{count} 篇");
        }
        tx.Commit();
    }
    private void Update(NoteRecord n, string body, SqliteTransaction tx)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "DELETE FROM notes WHERE id=$id"; c.Parameters.AddWithValue("$id", n.Id); c.ExecuteNonQuery();
        if (n.State != "ACTIVE") return;
        c.CommandText = "INSERT INTO notes VALUES($id,$title,$body,$updated)";
        c.Parameters.AddWithValue("$title", Rules.Fold(n.Title!)); c.Parameters.AddWithValue("$body", Rules.Fold(body));
        c.Parameters.AddWithValue("$updated", n.UpdatedAt.ToString("O")); c.ExecuteNonQuery();
        foreach (var tag in n.Tags!)
        {
            c.Parameters.Clear(); c.CommandText = "INSERT INTO tags VALUES($id,$tag)";
            c.Parameters.AddWithValue("$id", n.Id); c.Parameters.AddWithValue("$tag", Rules.Fold(tag)); c.ExecuteNonQuery();
        }
        foreach (var target in MarkdownLinks.Targets(body))
        {
            c.Parameters.Clear(); c.CommandText = "INSERT INTO links VALUES($id,$target)";
            c.Parameters.AddWithValue("$id", n.Id); c.Parameters.AddWithValue("$target", target); c.ExecuteNonQuery();
        }
    }
    public List<string> Search(string query, string[] tags, int offset, int limit)
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT id FROM notes WHERE (instr(title,$q)>0 OR instr(body,$q)>0)";
        c.Parameters.AddWithValue("$q", Rules.Fold(query));
        for (int i = 0; i < tags.Length; i++)
        {
            c.CommandText += $" AND EXISTS(SELECT 1 FROM tags WHERE note_id=notes.id AND tag=$t{i})";
            c.Parameters.AddWithValue($"$t{i}", Rules.Fold(tags[i]));
        }
        c.CommandText += " ORDER BY updated DESC,id LIMIT $limit OFFSET $offset";
        c.Parameters.AddWithValue("$limit", limit); c.Parameters.AddWithValue("$offset", offset);
        using var r = c.ExecuteReader(); var ids = new List<string>(); while (r.Read()) ids.Add(r.GetString(0)); return ids;
    }
    public List<string> Backlinks(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT source FROM links WHERE target=$id"; c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader(); var ids = new List<string>(); while (r.Read()) ids.Add(r.GetString(0)); return ids;
    }
    public void Dispose() => db.Dispose();
}
