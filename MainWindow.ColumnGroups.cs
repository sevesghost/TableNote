using Avalonia.Controls;

namespace TableNote;

/// <summary>Collapsible groups in the grid. MainWindow.axaml.cs only calls these hooks.</summary>
public partial class MainWindow
{
    /// <summary>Collapsed children keep their grid index at width 0 so selection math does not shift.</summary>
    private ColumnDefinition GroupColumnDefinition(int column)
    {
        var hidden = ColumnGroupOps.IsHidden(_session.Doc, column);
        var def = new ColumnDefinition(new GridLength(hidden ? 0 : _session.Doc.ColumnWidth(column)));
        if (hidden) { def.MinWidth = 0; def.MaxWidth = 0; }
        return def;
    }

    private GridLength GroupColumnLength(int column) =>
        new(ColumnGroupOps.IsHidden(_session.Doc, column) ? 0 : _session.Doc.ColumnWidth(column));

    /// <summary>Hidden child: no editor, so its text cannot be cleared from the view. The value stays in the row.</summary>
    private Control GroupCell(int row, int column, Avalonia.Media.IBrush bg) =>
        ColumnGroupOps.IsHidden(_session.Doc, column) ? HiddenColumnPlaceholder() : MakeCell(row, column, bg);

    private bool TryHiddenHeader(int column, out Control header)
    {
        if (!ColumnGroupOps.IsHidden(_session.Doc, column)) { header = null!; return false; }
        header = HiddenColumnPlaceholder();
        _headerBoxes.Add(header);
        return true;
    }

    /// <summary>Puts the ▸/▾ toggle in column 0 of the header grid when this column is a group parent. Returns the next free slot.</summary>
    private int AddGroupToggle(Grid grid, int column)
    {
        if (!ColumnGroupOps.IsParent(_session.Doc, column)) return 0;
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var toggle = MakeGroupToggle(column);
        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);
        return 1;
    }

    /// <summary>Collapsed child columns have no editor; land on the nearest column that is actually shown.</summary>
    private int NearestVisibleGroupColumn(int column)
    {
        var n = _session.Doc.Columns.Count;
        if (n == 0) return 0;
        column = Math.Clamp(column, 0, n - 1);
        if (!ColumnGroupOps.IsHidden(_session.Doc, column)) return column;
        for (var d = 1; d < n; d++)
        {
            if (column + d < n && !ColumnGroupOps.IsHidden(_session.Doc, column + d)) return column + d;
            if (column - d >= 0 && !ColumnGroupOps.IsHidden(_session.Doc, column - d)) return column - d;
        }
        return column;
    }

    private static Control HiddenColumnPlaceholder() =>
        new Border { Width = 0, MinWidth = 0, IsVisible = false, IsHitTestVisible = false };
}
