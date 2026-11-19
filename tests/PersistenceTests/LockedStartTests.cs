using System.Diagnostics;
using TableNote;

// note.json locked at start-up: the stand-in table is marked, and side copies never write over an earlier run's edits.
public static partial class T
{
    public static void TestLockedLoadIsPlaceholderThenOpens()
    {
        var p = Path.Combine(Dir("locked-start"), "note.json");
        var real = Sample();
        real.LaunchAtLogin = true;
        NoteStore.Save(p, real);
        LoadResult stand;
        using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stand = NoteStore.Load(p);
            Ok("locked note.json: CanRead is false", !NoteStore.CanRead(p));
            var quick = Stopwatch.StartNew();
            var again = NoteStore.Load(p, TableLimits.MaxSaveFileBytes, retries: 0);
            Ok("locked note.json: a retry with retries: 0 does not sleep", again.Placeholder && quick.ElapsedMilliseconds < 140, quick.ElapsedMilliseconds + " ms");
        }
        Ok("locked note.json: the result is a placeholder that keeps the file",
            stand.Placeholder && stand.PreserveExistingFile && stand.Document.Rows.All(r => r.All(string.IsNullOrEmpty)));
        Ok("locked note.json: the placeholder's launch-at-sign-in is only a default (not the user's)", !stand.Document.LaunchAtLogin);
        Ok("released note.json: CanRead is true", NoteStore.CanRead(p));
        var opened = NoteStore.Load(p, TableLimits.MaxSaveFileBytes, retries: 0);
        Ok("released note.json: the real note opens, not a placeholder",
            !opened.Placeholder && !opened.PreserveExistingFile && opened.Document.Rows[0][0] == "งาน 0" && opened.Document.LaunchAtLogin);

        var corrupt = Path.Combine(Dir("not-locked"), "note.json");
        File.WriteAllText(corrupt, "{ not json");
        var fresh = NoteStore.Load(corrupt);
        Ok("a corrupt note.json that could be backed up is a fresh note, not a placeholder", !fresh.Placeholder && !fresh.PreserveExistingFile);

        var undo = new UndoStack();
        undo.Push(new RowMoveAction("x", 0, 1));
        undo.Undo(Sample());
        undo.Clear();
        Ok("UndoStack.Clear forgets undo and redo", !undo.CanUndo && !undo.CanRedo);
    }

    public static void TestSideCopyKeepsAnEarlierRunsEdits()
    {
        var dir = Dir("side-keep");
        var p = Path.Combine(dir, "note.json");
        NoteStore.Save(p, Sample());
        var earlier = Sample(2);
        earlier.Rows[0][0] = "typed in an earlier run";
        NoteStore.Save(NoteStore.UnsavedPath(p), earlier); // left behind by an earlier stand-in session

        var now = Sample(3);
        now.Rows[0][0] = "typed in this run";
        var side = NoteStore.SaveSideCopy(p, now);
        Ok("side copy: an earlier run's note.unsaved.json is not written over",
            Path.GetFileName(side) != "note.unsaved.json" && Path.GetFileName(side).StartsWith("note.unsaved-", StringComparison.Ordinal) &&
            NoteStore.Load(NoteStore.UnsavedPath(p)).Document.Rows[0][0] == "typed in an earlier run", Path.GetFileName(side));
        now.Rows[0][0] = "typed again";
        Ok("side copy: later saves of this run go to the same file", NoteStore.SaveSideCopy(p, now) == side &&
            NoteStore.Load(side).Document.Rows[0][0] == "typed again");
    }

    public static void TestArchiveUnsavedAtStart()
    {
        var dir = Dir("side-archive");
        var p = Path.Combine(dir, "note.json");
        NoteStore.Save(p, Sample());
        Ok("archive: nothing to do without note.unsaved.json", NoteStore.ArchiveUnsaved(p) == null);

        var earlier = Sample(2);
        earlier.Rows[1][0] = "keep me";
        NoteStore.Save(NoteStore.UnsavedPath(p), earlier);
        var saved = File.GetLastWriteTime(NoteStore.UnsavedPath(p));
        var kept = NoteStore.ArchiveUnsaved(p);
        Ok("archive: note.unsaved.json is renamed after its own time, content intact",
            kept is { } k && k.FileName == $"note.unsaved-{saved:yyyyMMdd-HHmmss}.json" && !File.Exists(NoteStore.UnsavedPath(p)) &&
            NoteStore.Load(Path.Combine(dir, k.FileName)).Document.Rows[1][0] == "keep me", kept?.FileName ?? "null");

        NoteStore.Save(NoteStore.UnsavedPath(p), earlier);
        File.SetLastWriteTime(NoteStore.UnsavedPath(p), saved);
        var second = NoteStore.ArchiveUnsaved(p);
        Ok("archive: an existing archive with the same time is never replaced",
            second is { } s && s.FileName != kept?.FileName && File.Exists(Path.Combine(dir, kept!.Value.FileName)), second?.FileName ?? "null");
        Ok("archive: renamed side copies are not rolling backups (never pruned)",
            !BackupRetention.IsRolling(kept!.Value.FileName) && !BackupRetention.IsRolling(second!.Value.FileName));
    }
}
