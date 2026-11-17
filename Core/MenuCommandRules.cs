namespace TableNote;

/// <summary>
/// Enable and disable rules for the column, row, and detail commands.
/// The header context menu and the top menu bar both use these, so a command is
/// offered only when it has a valid target. Status and Due are system columns and
/// cannot become a collapsible group. The Due column cannot be deleted.
/// </summary>
public static class MenuCommandRules
{
    public static bool HasColumn(int column, int count) => column >= 0 && column < count;

    public static bool HasRow(int row, int count) => row >= 0 && row < count;

    /// <summary>Cut / Copy / Paste on the header name box. No box, or no column, means no target.</summary>
    public static bool ColumnClipboard(bool edit, bool hasHeaderBox, int column, int count) =>
        edit && hasHeaderBox && HasColumn(column, count);

    public static bool MoveLeft(bool edit, int column, int count) =>
        edit && HasColumn(column, count) && column > 0;

    public static bool MoveRight(bool edit, int column, int count) =>
        edit && HasColumn(column, count) && column < count - 1;

    public static bool MoveFirst(bool edit, int column, int count) => MoveLeft(edit, column, count);

    public static bool MoveLast(bool edit, int column, int count) => MoveRight(edit, column, count);

    public static bool ColumnSettings(bool edit, int column, int count) =>
        edit && HasColumn(column, count);

    public static bool DetailColumnToggle(bool edit, int column, int count) =>
        edit && HasColumn(column, count);

    public static bool DetailCellToggle(bool edit, int column, int count, int rowCount) =>
        edit && HasColumn(column, count) && rowCount > 0;

    public static bool ColumnWidth(bool edit, int column, int count, bool hidden) =>
        edit && HasColumn(column, count) && !hidden;

    /// <summary>Plain column only. System columns (Status, Due) and a full table cannot start a group.</summary>
    public static bool MakeGroup(bool edit, int column, int count, bool system, int room) =>
        edit && HasColumn(column, count) && room >= 1 && !system;

    public static bool DeleteColumn(bool edit, int column, int count, bool isDue) =>
        edit && HasColumn(column, count) && !isDue && count > 1;

    /// <summary>
    /// Table-menu spot <c>spot:add-status</c>. Same rule as the + Column menu:
    /// enabled only when the status column is not already in the table.
    /// </summary>
    public static bool AddStatusColumn(NoteDocument doc) => doc.IndexOfRole(ColumnRoles.Status) < 0;

    /// <summary>
    /// Table-menu spot <c>spot:add-alert-date</c>. Same rule as the + Column menu:
    /// enabled only when the reminder date column is not already in the table.
    /// </summary>
    public static bool AddDueColumn(NoteDocument doc) => doc.IndexOfRole(ColumnRoles.Due) < 0;

    public static bool ToggleGroup(bool edit) => edit;

    public static bool AddChild(bool edit, int room, int childCount) =>
        edit && room >= 1 && childCount < ColumnGroupOps.MaxChildCount;

    public static bool RenameChildren(bool edit) => edit;

    public static bool RemoveChildParent(bool edit) => edit;

    public static bool RemoveNamedChild(bool edit) => edit;

    public static bool Ungroup(bool edit) => edit;

    /// <summary>"Child of …" is a label, never a command.</summary>
    public static bool ChildOfLabel() => false;

    public static bool RemoveThisColumn(bool edit, int count) => edit && count > 1;

    public static bool RowMove(bool edit, bool busy, IReadOnlyList<int> visible, int row, RowMoveKind kind) =>
        edit && !busy && HasRow(row, int.MaxValue) && RowOps.TargetFor(visible, row, kind) >= 0;

    public static bool RowStyleMenu(bool edit, bool hasRow) => edit && hasRow;

    public static bool RowHighlight(bool edit, bool hasRow) => edit && hasRow;

    public static bool RowClear(bool edit, bool hasRow, bool hasStyle) => edit && hasRow && hasStyle;

    public static bool DetailRowToggle(bool edit, bool hasRow, bool hasColumn) => edit && hasRow && hasColumn;

    public static bool RowHeight(bool edit, bool hasRow) => edit && hasRow;

    /// <summary>Open is available whenever the cell shows the detail button, including background mode.</summary>
    public static bool DetailOpen(bool showsButton) => showsButton;

    public static bool DetailHide(bool edit, bool showsButton) => edit && showsButton;
}
