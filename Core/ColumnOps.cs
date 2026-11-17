namespace TableNote;

/// <summary>v2.4.0: move a column. Every list that runs parallel to <see cref="NoteDocument.Columns"/>
/// must move together, so status/due (found by role) and per-column settings stay on the same column.</summary>
public static class ColumnOps
{
    public static bool MoveColumn(this NoteDocument doc, int from, int to)
    {
        var n = doc.Columns.Count;
        if (from < 0 || from >= n || to < 0 || to >= n || from == to) return false;
        doc.EnsureColumnRoles();
        doc.EnsureColumnIds();
        while (doc.ColumnWidths.Count < n) doc.ColumnWidths.Add(TableLimits.DefaultColumnWidth);
        // A parent column drags its children with it (they stay in group order, data included).
        // Moving any other column — including a child — stays a single-column move; membership is by id.
        var group = ColumnGroupOps.GroupForParent(doc, from);
        if (group != null && (group.ChildIds?.Count ?? 0) > 0)
            return MoveGroupBlock(doc, group, to);
        MoveSingle(doc, from, to);
        return true;
    }

    private static void MoveSingle(NoteDocument doc, int from, int to)
    {
        var n = doc.Columns.Count;
        MoveItem(doc.Columns, from, to);
        MoveItem(doc.ColumnRolesList, from, to);
        MoveItem(doc.ColumnWidths, from, to);
        MoveItem(doc.ColumnIds, from, to); // columnSettings is keyed by id → nothing to move
        foreach (var row in doc.Rows)
        {
            while (row.Count < n) row.Add("");
            MoveItem(row, from, to);
        }
    }

    /// <summary>Place the parent at <paramref name="to"/> (clamped so the children fit) and pack children right after it.</summary>
    private static bool MoveGroupBlock(NoteDocument doc, ColumnGroup group, int to)
    {
        var members = ColumnGroupOps.MemberIndices(doc, group);
        var n = doc.Columns.Count;
        if (members.Count <= 1)
        {
            MoveSingle(doc, members.Count == 1 ? members[0] : 0, to);
            return true;
        }
        var block = new HashSet<int>(members);
        var outside = Enumerable.Range(0, n).Where(i => !block.Contains(i)).ToList();
        var start = Math.Clamp(to, 0, n - members.Count);
        var order = new int[n];
        Array.Fill(order, -1);
        for (var i = 0; i < members.Count; i++) order[start + i] = members[i];
        var oi = 0;
        for (var i = 0; i < n; i++)
            if (order[i] < 0) order[i] = outside[oi++];
        if (order.SequenceEqual(Enumerable.Range(0, n))) return false;
        ApplyOrder(doc, order);
        return true;
    }

    private static void ApplyOrder(NoteDocument doc, IReadOnlyList<int> order)
    {
        doc.Columns = order.Select(i => doc.Columns[i]).ToList();
        doc.ColumnRolesList = order.Select(i => doc.ColumnRolesList[i]).ToList();
        doc.ColumnWidths = order.Select(i => i < doc.ColumnWidths.Count ? doc.ColumnWidths[i] : TableLimits.DefaultColumnWidth).ToList();
        doc.ColumnIds = order.Select(i => doc.ColumnIds[i]).ToList();
        foreach (var row in doc.Rows)
        {
            var tail = row.Count > order.Count ? row.Skip(order.Count).ToList() : null;
            var copy = order.Select(i => i < row.Count ? row[i] ?? "" : "").ToList();
            row.Clear();
            row.AddRange(copy);
            if (tail != null) row.AddRange(tail);
        }
    }

    internal static void MoveItem<T>(List<T> list, int from, int to)
    {
        if (from >= list.Count) return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Min(to, list.Count), item);
    }

    /// <summary>Where a cell index ends up after moving column from → to.</summary>
    public static int Follow(int col, int from, int to)
    {
        if (col == from) return to;
        if (from < to && col > from && col <= to) return col - 1;
        if (from > to && col >= to && col < from) return col + 1;
        return col;
    }
}

public sealed partial class TableSession
{
    /// <summary>v2.4.0: move a column; the active cell follows the column it was in.</summary>
    public EditResult MoveColumn(int from, int to)
    {
        if (from < 0 || from >= Doc.Columns.Count) return new EditResult(false, Localizer.T("colmove.none"));
        to = Math.Clamp(to, 0, Doc.Columns.Count - 1);
        if (from == to) return new EditResult(false, null);
        // Follow the cells by column id so a parent that moves with its children keeps the active cell.
        string? activeId = ActiveCol >= 0 && ActiveCol < Doc.Columns.Count ? Doc.ColumnIdAt(ActiveCol) : null;
        string? anchorId = AnchorCol >= 0 && AnchorCol < Doc.Columns.Count ? Doc.ColumnIdAt(AnchorCol) : null;
        if (!Doc.MoveColumn(from, to))
            return new EditResult(false, ColumnGroupOps.GroupForParent(Doc, from) != null ? null : Localizer.T("colmove.failed"));
        if (activeId != null)
        {
            var i = Doc.ColumnIds.IndexOf(activeId);
            if (i >= 0) ActiveCol = i;
        }
        if (anchorId != null)
        {
            var i = Doc.ColumnIds.IndexOf(anchorId);
            if (i >= 0) AnchorCol = i;
        }
        return new EditResult(true, null);
    }

    /// <summary>v2.4.0: delete a given column from the header menu. Always asks, even for an empty column.
    /// Status and the reminder date column can be deleted here (DeletePlanner.ForColumn still reports the old due block).</summary>
    public async Task<EditResult> DeleteColumnAtAsync(int column, Func<string, Task<bool>> confirm)
    {
        if (column < 0 || column >= Doc.Columns.Count) return new EditResult(false, Localizer.T("delete.no_column"));
        var plan = SystemColumnToggle.PlanHeaderDelete(Doc, column);
        if (!plan.CanDelete) return new EditResult(false, plan.StatusIfBlocked);
        Doc.EnsureColumnRoles();
        var name = Doc.Columns[column];
        var role = Doc.ColumnRolesList.ElementAtOrDefault(column) ?? "";
        var message = plan.NeedsConfirm ? plan.ConfirmMessage : Localizer.T("delete.column_confirm", DeletePlanner.ColumnLabel(Doc, column));
        if (!await confirm(message)) return new EditResult(false, Localizer.T("delete.canceled"));
        Doc.EnsureColumnRoles();
        if (column >= Doc.Columns.Count || Doc.Columns[column] != name || (Doc.ColumnRolesList.ElementAtOrDefault(column) ?? "") != role)
        {
            var hits = Enumerable.Range(0, Doc.Columns.Count)
                .Where(i => Doc.Columns[i] == name && (Doc.ColumnRolesList.ElementAtOrDefault(i) ?? "") == role).ToList();
            if (hits.Count != 1) return new EditResult(false, Localizer.T("delete.canceled_column_changed"));
            column = hits[0];
        }
        if (!Doc.RemoveColumn(column)) return new EditResult(false, DeletePlanner.DueColumnProtected);
        if (HasSelection)
        {
            if (ActiveCol > column) ActiveCol--;
            else if (ActiveCol >= Doc.Columns.Count) ActiveCol = Doc.Columns.Count - 1;
            if (AnchorCol > column) AnchorCol--;
            else if (AnchorCol >= Doc.Columns.Count) AnchorCol = Doc.Columns.Count - 1;
        }
        return new EditResult(true, null);
    }
}
