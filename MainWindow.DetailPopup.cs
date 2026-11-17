using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// Detail button on chosen cells or whole columns. The button opens <see cref="CellDetailDialog"/>.
/// Menu items are added by wrapping the existing column and row menu hooks, so other features keep theirs.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush DetailButtonBg = new SolidColorBrush(Color.Parse("#F0D78A"));
    private static readonly IBrush DetailButtonOn = new SolidColorBrush(Color.Parse("#E7B84A"));
    private static readonly IBrush DetailButtonBorder = new SolidColorBrush(Color.Parse("#C47B2B"));
    private static readonly IBrush DetailButtonFg = new SolidColorBrush(Color.Parse("#3B3222"));

    /// <summary>Call once after the other menu features have registered, so their items are kept.</summary>
    private void InitDetailPopup()
    {
        var prevCol = _extraColumnMenuItems;
        _extraColumnMenuItems = column => Join(prevCol?.Invoke(column), DetailColumnMenu(column));
        var prevRow = _extraRowMenuItems;
        _extraRowMenuItems = rowId => Join(prevRow?.Invoke(rowId), DetailRowMenu(rowId));
    }

    private static IEnumerable<Control> Join(IEnumerable<Control>? first, IEnumerable<Control> second)
    {
        if (first != null) foreach (var c in first) yield return c;
        foreach (var c in second) yield return c;
    }

    /// <summary>Wrap a data cell with the detail button when this cell or its column is selected for one.</summary>
    private Control AttachDetailButton(int row, int column, Control inner)
    {
        if (!CellDetailOps.ShowsButton(_session.Doc, row, column)) return inner;
        var table = CellDetailOps.Get(_session.Doc, _session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column));
        var filled = table != null && CellDetailOps.IsTable(table) && !IsEmptyTable(table);
        var button = new Button
        {
            Name = CellDetailOps.ButtonName,
            Content = Localizer.T("detail.button"),
            FontFamily = AppFonts.Thai,
            FontSize = 12,
            Background = filled ? DetailButtonOn : DetailButtonBg,
            Foreground = DetailButtonFg,
            BorderBrush = DetailButtonBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(2, 2, 2, 2),
            MinWidth = 44,
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = true
        };
        var tip = table != null && CellDetailOps.IsTable(table)
            ? Localizer.T("detail.button_tip", table.Columns.Count, table.Rows.Count)
            : Localizer.T("detail.button_tip_empty");
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            _session.ChooseCell(row, column);
            OpenDetailPopup(row, column);
        };

        button.ContextMenu = new ContextMenu { ItemsSource = BuildDetailButtonMenuItems(row, column) };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(inner);
        grid.Children.Add(button);
        return grid;
    }

    private static bool IsEmptyTable(CellDetail table) =>
        table.Columns.All(string.IsNullOrWhiteSpace) && table.Rows.All(r => r.All(string.IsNullOrWhiteSpace));

    private IEnumerable<Control> DetailColumnMenu(int column)
    {
        var edit = _view == ViewMode.Edit;
        var columnItem = new MenuItem
        {
            FontFamily = AppFonts.Thai,
            IsEnabled = MenuCommandRules.DetailColumnToggle(edit, column, _session.Doc.Columns.Count)
        };
        void RefreshColumn()
        {
            var on = CellDetailOps.ColumnButtonOn(_session.Doc, column);
            columnItem.Header = (on ? "✓ " : "") + Localizer.T("detail.column_toggle");
        }
        RefreshColumn();
        columnItem.AttachedToVisualTree += (_, _) => RefreshColumn();
        columnItem.Click += (_, _) =>
        {
            var on = !CellDetailOps.ColumnButtonOn(_session.Doc, column);
            ToggleColumnButton(column, on);
        };

        var cellItem = new MenuItem
        {
            FontFamily = AppFonts.Thai,
            IsEnabled = MenuCommandRules.DetailCellToggle(edit, column, _session.Doc.Columns.Count, _session.Doc.Rows.Count)
        };
        void RefreshCell()
        {
            var row = _session.TargetRow();
            var on = row >= 0 && CellDetailOps.ShowsButton(_session.Doc, row, column);
            cellItem.Header = (on ? "✓ " : "") + Localizer.T("detail.cell_toggle");
        }
        RefreshCell();
        cellItem.AttachedToVisualTree += (_, _) => RefreshCell();
        cellItem.Click += (_, _) =>
        {
            var row = _session.TargetRow();
            if (row < 0) return;
            ToggleCellButton(row, column, !CellDetailOps.ShowsButton(_session.Doc, row, column));
        };
        return [columnItem, cellItem];
    }

    /// <summary>Open / Hide from the detail button. The top menu uses the same pair for the selected cell.</summary>
    internal List<Control> BuildDetailButtonMenuItems(int row, int column)
    {
        var shows = CellDetailOps.ShowsButton(_session.Doc, row, column);
        var open = new MenuItem
        {
            Header = Localizer.T("detail.open"),
            FontFamily = AppFonts.Thai,
            IsEnabled = MenuCommandRules.DetailOpen(shows)
        };
        open.Click += (_, _) => OpenDetailPopup(row, column);
        var hide = new MenuItem
        {
            Header = Localizer.T("detail.hide_cell"),
            FontFamily = AppFonts.Thai,
            IsEnabled = MenuCommandRules.DetailHide(_view == ViewMode.Edit, shows)
        };
        hide.Click += (_, _) => ToggleCellButton(row, column, false);
        return [open, hide];
    }

    private IEnumerable<Control> DetailRowMenu(string rowId)
    {
        var row = _session.Doc.RowIds.IndexOf(rowId);
        if (row < 0 || _session.Doc.Columns.Count == 0)
        {
            // The row context menu omits this item when the row is gone or there is no column.
            // The top menu still lists it, disabled, so the command stays visible with no target.
            if (rowId.Length == 0)
            {
                yield return new MenuItem
                {
                    Header = Localizer.T("detail.row_toggle", "—"),
                    FontFamily = AppFonts.Thai,
                    IsEnabled = MenuCommandRules.DetailRowToggle(false, false, false)
                };
            }
            yield break;
        }
        var column = _session.ActiveCol >= 0 && _session.ActiveCol < _session.Doc.Columns.Count ? _session.ActiveCol : 0;
        var name = _session.Doc.Columns[column];
        var on = CellDetailOps.ShowsButton(_session.Doc, row, column);
        var item = new MenuItem
        {
            Header = (on ? "✓ " : "") + Localizer.T("detail.row_toggle", name),
            FontFamily = AppFonts.Thai,
            IsEnabled = MenuCommandRules.DetailRowToggle(_view == ViewMode.Edit, true, true)
        };
        var col = column;
        item.Click += (_, _) => ToggleCellButton(row, col, !CellDetailOps.ShowsButton(_session.Doc, row, col));
        yield return item;
    }

    private void ToggleColumnButton(int column, bool on)
    {
        if (_view != ViewMode.Edit || column < 0 || column >= _session.Doc.Columns.Count) return;
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.detail_button"), () =>
        {
            if (!CellDetailOps.SetColumnButton(_session.Doc, column, on)) return Task.CompletedTask;
            Rebuild();
            FlushSave();
            StatusText.Text = Localizer.T(on ? "detail.column_on" : "detail.column_off", _session.Doc.Columns[column]);
            return Task.CompletedTask;
        }));
    }

    private void ToggleCellButton(int row, int column, bool on)
    {
        if (_view != ViewMode.Edit) return;
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.detail_button"), () =>
        {
            if (row < 0 || column < 0 || row >= _session.Doc.Rows.Count || column >= _session.Doc.Columns.Count)
                return Task.CompletedTask;
            if (!CellDetailOps.SetCellButton(_session.Doc, row, column, on)) return Task.CompletedTask;
            Rebuild();
            FlushSave();
            StatusText.Text = Localizer.T(on ? "detail.cell_on" : "detail.cell_off");
            return Task.CompletedTask;
        }));
    }

    private void OpenDetailPopup(int row, int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.detail_table"), () => OpenDetailAsync(row, column)));

    private async Task OpenDetailAsync(int row, int column)
    {
        var doc = _session.Doc;
        if (row < 0 || column < 0 || row >= doc.Rows.Count || column >= doc.Columns.Count) return;
        var rowId = doc.RowIdAt(row);
        var colId = doc.ColumnIdAt(column);
        var existing = CellDetailOps.Get(doc, rowId, colId);
        var seed = existing == null ? CellDetailOps.CreateDefault() : CellDetailOps.Clone(existing);
        var readOnly = _view != ViewMode.Edit;
        var dlg = new CellDetailDialog(Localizer.T("detail.title", doc.Columns[column], row + 1), seed, readOnly);
        await dlg.ShowDialog(this);
        if (!dlg.Saved || dlg.Result == null) return;
        if (!doc.RowIds.Contains(rowId) || !doc.ColumnIds.Contains(colId))
        {
            StatusText.Text = Localizer.T("detail.gone");
            return;
        }
        CellDetailOps.Set(doc, rowId, colId, dlg.Result);
        Rebuild();
        FlushSave();
        StatusText.Text = Localizer.T("detail.saved", dlg.Result.Columns.Count, dlg.Result.Rows.Count);
    }
}
