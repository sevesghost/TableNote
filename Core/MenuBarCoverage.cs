namespace TableNote;

/// <summary>
/// Every column, row, and detail command the top menu bar must expose.
/// The ids match the context-menu baseline. Tray-only commands are not in this list.
/// The menu bar builds them by calling the same methods as the context menus
/// (<c>BuildColumnMenuItems</c>, <c>CreateRowMenuItems</c>, <c>BuildDetailButtonMenuItems</c>).
/// </summary>
public static class MenuBarCoverage
{
    public static readonly string[] ColumnIds =
    [
        "col.cut",
        "col.copy",
        "col.paste",
        "col.move_left",
        "col.move_right",
        "col.move_first",
        "col.move_last",
        "colset.menu",
        "detail.column_toggle",
        "detail.cell_toggle",
        "size.col_menu",
        "colgroup.make",
        "colgroup.collapse",
        "colgroup.expand",
        "colgroup.add_child",
        "colgroup.rename_children",
        "colgroup.remove_child",
        "colgroup.remove_named",
        "colgroup.ungroup",
        "colgroup.child_of",
        "col.delete",
    ];

    public static readonly string[] RowIds =
    [
        "row.move_up",
        "row.move_down",
        "row.move_top",
        "row.move_bottom",
        "rowstyle.colour",
        "rowstyle.none",
        "colour.yellow",
        "colour.orange",
        "colour.red",
        "colour.pink",
        "colour.purple",
        "colour.blue",
        "colour.green",
        "colour.gray",
        "rowstyle.highlight",
        "rowstyle.clear",
        "detail.row_toggle",
        "size.row_menu",
    ];

    public static readonly string[] DetailButtonIds =
    [
        "detail.open",
        "detail.hide_cell",
    ];

    public static IEnumerable<string> All()
    {
        foreach (var id in ColumnIds) yield return id;
        foreach (var id in RowIds) yield return id;
        foreach (var id in DetailButtonIds) yield return id;
    }
}
