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

    /// <summary>2.6.0: drag of several selected rows. <paramref name="rows"/> (document indexes, visible) move together, in
    /// their order, into the gap <paramref name="slot"/> of the VISIBLE list; hidden rows stay where they are. False when
    /// the gap is inside or next to the block (nothing to do).</summary>
    public static bool MoveRowsToSlot(NoteDocument doc, IReadOnlyList<int> visible, IReadOnlyList<int> rows, int slot)
    {
        doc.EnsureRowIds();
        var moving = rows.Where(r => r >= 0 && r < doc.Rows.Count).Distinct().OrderBy(r => r).ToList();
        if (moving.Count == 0 || slot < 0 || slot > visible.Count) return false;
        var vis = moving.Select(r => IndexOf(visible, r)).Where(i => i >= 0).ToList();
        if (vis.Count == 0) return false;
        if (slot >= vis.Min() && slot <= vis.Max() + 1) return false; // dropped onto the block itself
        var ids = moving.Select(r => doc.RowIds[r]).ToList();
        var movingSet = new HashSet<string>(ids, StringComparer.Ordinal);
        var rest = doc.RowIds.Where(id => !movingSet.Contains(id)).ToList();
        int at;
        if (slot < visible.Count) at = rest.IndexOf(doc.RowIds[visible[slot]]);          // before that visible row
        else at = rest.IndexOf(doc.RowIds[visible[^1]]) + 1;                              // after the last visible row
        if (at < 0) return false;
        rest.InsertRange(at, ids);
        ApplyOrder(doc, rest);
        return true;
    }

    /// <summary>Puts the rows in the order of <paramref name="order"/> (row ids). Rows not listed keep their order after them.</summary>
    public static void ApplyOrder(NoteDocument doc, IReadOnlyList<string> order)
    {
        doc.EnsureRowIds();
        var pos = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++) pos.TryAdd(order[i], i);
        var sorted = doc.RowIds.Select((id, i) => (id, row: doc.Rows[i], i))
            .OrderBy(x => pos.TryGetValue(x.id, out var p) ? p : int.MaxValue).ThenBy(x => x.i).ToList();
        doc.RowIds = sorted.Select(x => x.id).ToList();
        doc.Rows = sorted.Select(x => x.row).ToList();
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

/// <summary>2.6.0: undo step for moving several rows at once: the row order before and after (row ids).</summary>
public sealed class RowOrderAction(List<string> before, List<string> after, string label) : IUndoAction
{
    public string Label => label;
    public long Bytes => (before.Count + after.Count) * 40L + 64;
    public void Undo(NoteDocument doc) => RowOps.ApplyOrder(doc, before);
    public void Redo(NoteDocument doc) => RowOps.ApplyOrder(doc, after);
}
