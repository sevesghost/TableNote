namespace TableNote;

/// <summary>
/// v2.4.0: move a row to another position. Rows and RowIds always move together at the same index,
/// so everything keyed by row id (Snoozes / Recurrence / Reminders / Acknowledged) stays attached to its row.
/// note.json format is unchanged — only the order of "rows" / "rowIds" changes.
/// </summary>
public static class RowOps
{
    /// <summary>Move row <paramref name="from"/> so it ends up at index <paramref name="to"/> (final position).</summary>
    public static bool MoveRow(NoteDocument doc, int from, int to)
    {
        doc.EnsureRowIds();
        var n = doc.Rows.Count;
        if (from < 0 || from >= n || n == 0) return false;
        to = Math.Clamp(to, 0, n - 1);
        if (to == from) return false;
        var row = doc.Rows[from];
        var id = doc.RowIds[from];
        doc.Rows.RemoveAt(from);
        doc.RowIds.RemoveAt(from);
        doc.Rows.Insert(to, row);
        doc.RowIds.Insert(to, id);
        return true;
    }

    /// <summary>
    /// Target index for a one-step / to-edge move while only <paramref name="visible"/> rows are shown
    /// (Hide done): "up" jumps over hidden rows to the previous VISIBLE row's position. -1 = nothing to do.
    /// </summary>
    public static int TargetFor(IReadOnlyList<int> visible, int row, RowMoveKind kind)
    {
        var vi = IndexOf(visible, row);
        if (vi < 0 || visible.Count < 2) return -1;
        return kind switch
        {
            RowMoveKind.Up => vi > 0 ? visible[vi - 1] : -1,
            RowMoveKind.Down => vi < visible.Count - 1 ? visible[vi + 1] : -1,
            RowMoveKind.Top => vi > 0 ? visible[0] : -1,
            RowMoveKind.Bottom => vi < visible.Count - 1 ? visible[^1] : -1,
            _ => -1
        };
    }

    /// <summary>
    /// Drag & drop: <paramref name="slot"/> is the gap in the VISIBLE list (0 = before the first visible row,
    /// visible.Count = after the last). Returns the final document index for <paramref name="row"/>, or -1.
    /// </summary>
    public static int TargetForSlot(IReadOnlyList<int> visible, int row, int slot)
    {
        var vi = IndexOf(visible, row);
        if (vi < 0 || slot < 0 || slot > visible.Count) return -1;
        if (slot == vi || slot == vi + 1) return -1; // dropped onto its own place
        // Gap before visible[slot] (or after the last visible row), expressed in document indices.
        var before = slot < visible.Count ? visible[slot] : visible[^1] + 1;
        return before > row ? before - 1 : before; // removing the row first shifts later indices by one
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }
}

public enum RowMoveKind { Up, Down, Top, Bottom }

/// <summary>
/// Undo step for a row move. Not a SnapshotAction: Snapshot.Restore keeps the CURRENT row order, so it could
/// never undo a reorder. Bound to the row id; positions are re-found at undo/redo time.
/// </summary>
public sealed class RowMoveAction(string rowId, int from, int to, string? label = null) : IUndoAction
{
    public string Label => label ?? Localizer.T("undo.move_row"); // v2.4.2: default resolved when shown
    public string RowId => rowId;
    public int From => from;
    public int To => to;

    public void Undo(NoteDocument doc) => MoveTo(doc, from);
    public void Redo(NoteDocument doc) => MoveTo(doc, to);

    private void MoveTo(NoteDocument doc, int index)
    {
        doc.EnsureRowIds();
        var i = doc.RowIds.IndexOf(rowId);
        if (i >= 0) RowOps.MoveRow(doc, i, index); // row gone (deleted later outside undo order) → skip
    }
}
