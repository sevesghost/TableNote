namespace TableNote;

public sealed partial class TableSession
{
    public NoteDocument Doc { get; }
    public int ActiveRow { get; private set; }
    public int ActiveCol { get; private set; }
    public int AnchorRow { get; private set; }
    public int AnchorCol { get; private set; }
    public bool HasSelection { get; private set; }

    public TableSession(NoteDocument document) => Doc = document;

    public void ChooseCell(int row, int column)
    {
        ActiveRow = AnchorRow = row;
        ActiveCol = AnchorCol = column;
        HasSelection = true;
    }

    public void ExtendTo(int row, int column)
    {
        ActiveRow = row;
        ActiveCol = column;
        HasSelection = true;
    }

    public void ChooseRow(int row)
    {
        ActiveRow = row;
        HasSelection = true;
        if (ActiveCol < 0 || ActiveCol >= Doc.Columns.Count) ActiveCol = 0;
    }

    public bool SetCell(int row, int column, string? value)
    {
        if (row < 0 || row >= Doc.Rows.Count || column < 0 || column >= Doc.Columns.Count) return false;
        value ??= "";
        if (value.Length > TableLimits.MaxCellChars) return false;
        while (Doc.Rows[row].Count < Doc.Columns.Count) Doc.Rows[row].Add("");
        Doc.Rows[row][column] = value;
        return true;
    }

    public bool SetHeader(int column, string? value)
    {
        if (column < 0 || column >= Doc.Columns.Count) return false;
        value ??= "";
        if (value.Length > TableLimits.MaxCellChars) return false;
        Doc.Columns[column] = value;
        return true;
    }

    public string? AddRow()
    {
        var after = HasSelection && ActiveRow >= 0 && ActiveRow < Doc.Rows.Count ? ActiveRow : Doc.Rows.Count - 1;
        if (!Doc.InsertRowAfter(after)) return TableLimits.RowLimitMessage;
        ActiveRow = Math.Clamp(after + 1, 0, Math.Max(0, Doc.Rows.Count - 1));
        if (ActiveCol < 0) ActiveCol = 0;
        HasSelection = true;
        return null;
    }

    public string? AddColumn()
    {
        if (!Doc.AddColumn()) return TableLimits.ColumnLimitMessage;
        ActiveCol = Doc.Columns.Count - 1;
        if (Doc.Rows.Count > 0 && ActiveRow < 0) ActiveRow = 0;
        HasSelection = true;
        return null;
    }

    public int TargetRow() =>
        HasSelection && ActiveRow >= 0 && ActiveRow < Doc.Rows.Count ? ActiveRow : Doc.Rows.Count - 1;

    public int TargetColumn() =>
        HasSelection && ActiveCol >= 0 && ActiveCol < Doc.Columns.Count ? ActiveCol : Doc.Columns.Count - 1;

    public async Task<EditResult> DeleteRowAsync(Func<string, Task<bool>> confirm, Action<int>? beforeRemove = null)
    {
        var row = TargetRow();
        var plan = DeletePlanner.ForRow(Doc, row);
        if (!plan.CanDelete) return new EditResult(false, plan.StatusIfBlocked);
        // Bind to the row id, not the index: rows may be inserted/removed/sorted while the dialog is open.
        var rowId = Doc.RowIdAt(row);
        if (plan.NeedsConfirm && !await confirm(plan.ConfirmMessage))
            return new EditResult(false, Localizer.T("delete.canceled"));
        Doc.EnsureRowIds();
        row = Doc.RowIds.IndexOf(rowId);
        if (row < 0 || row >= Doc.Rows.Count)
            return new EditResult(false, Localizer.T("delete.canceled_row_changed"));
        beforeRemove?.Invoke(row); // e.g. capture undo from the row as it is now
        Doc.RemoveRow(row);
        if (HasSelection)
        {
            if (Doc.Rows.Count == 0) ActiveRow = -1;
            else if (ActiveRow > row) ActiveRow--;
            else if (ActiveRow >= Doc.Rows.Count) ActiveRow = Doc.Rows.Count - 1;
        }
        return new EditResult(true, null);
    }

    public async Task<EditResult> DeleteColumnAsync(Func<string, Task<bool>> confirm)
    {
        var column = TargetColumn();
        var plan = DeletePlanner.ForColumn(Doc, column);
        if (!plan.CanDelete) return new EditResult(false, plan.StatusIfBlocked);
        // v2.3.1 (L5): bind to the column's header + role, re-find it after the dialog (like rows do by id).
        Doc.EnsureColumnRoles();
        var name = Doc.Columns[column];
        var role = Doc.ColumnRolesList.ElementAtOrDefault(column) ?? "";
        if (plan.NeedsConfirm && !await confirm(plan.ConfirmMessage))
            return new EditResult(false, Localizer.T("delete.canceled"));
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
        }
        return new EditResult(true, null);
    }
}
