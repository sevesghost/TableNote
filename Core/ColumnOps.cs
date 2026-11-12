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
        MoveItem(doc.Columns, from, to);
        MoveItem(doc.ColumnRolesList, from, to);
        MoveItem(doc.ColumnWidths, from, to);
        MoveItem(doc.ColumnIds, from, to); // columnSettings is keyed by id → nothing to move
        foreach (var row in doc.Rows)
        {
            while (row.Count < n) row.Add("");
            MoveItem(row, from, to);
        }
        return true;
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
        if (!Doc.MoveColumn(from, to)) return new EditResult(false, Localizer.T("colmove.failed"));
        ActiveCol = ColumnOps.Follow(ActiveCol, from, to);
        AnchorCol = ColumnOps.Follow(AnchorCol, from, to);
        return new EditResult(true, null);
    }

    /// <summary>v2.4.0: delete a given column from the header menu. Always asks, even for an empty column;
    /// the due column stays protected (DeletePlanner / RemoveColumn).</summary>
    public async Task<EditResult> DeleteColumnAtAsync(int column, Func<string, Task<bool>> confirm)
    {
        if (column < 0 || column >= Doc.Columns.Count) return new EditResult(false, Localizer.T("delete.no_column"));
        var plan = DeletePlanner.ForColumn(Doc, column);
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
