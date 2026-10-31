namespace Zhijian.Core;

public sealed partial class Workspace
{
    public NoteRecord SetMarks(string id, long revision, bool pinned, bool favorite)
    {
        lock (gate)
        {
            Ensure(); var note = Current(id, revision);
            if (note.State != "ACTIVE") throw new InvalidOperationException("请先恢复回收站笔记。");
            ReadBody(note);
            return Commit(note with { Revision = note.Revision + 1, Pinned = pinned, Favorite = favorite }, null);
        }
    }

    public void SetLayout(double listWidth, double editorRatio)
    {
        lock (gate)
        {
            Ensure();
            if (!double.IsFinite(listWidth) || !double.IsFinite(editorRatio) || listWidth is < 220 or > 380 || editorRatio is < 0.2 or > 0.8)
                throw new ArgumentException("分栏尺寸超出范围。");
            var next = Settings with { ListWidth = listWidth, EditorRatio = editorRatio };
            Disk.Atomic(At("settings.json"), Disk.Serialize(next)); Settings = next;
        }
    }
}
