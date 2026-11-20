using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>
/// 2.6.0: whole rows and columns, like Excel. A click on a row number selects the row, a click on a column header the
/// column; dragging or Shift+click selects several. Right-click: insert as many rows / columns as are selected (above or
/// below, left or right), delete them, or "insert copied" — rows or columns copied in TableNote come back with their
/// colour, repeat, reminders, or type and width; text from Excel goes in as new rows. A selected row number can be
/// dragged to move the selected rows. A header is renamed with a double-click (or F2). Each change is one undo step.
/// </summary>
public partial class MainWindow
{
    private enum DragKind { Cells, Rows, Columns }

    private DragKind _dragKind = DragKind.Cells;
    /// <summary>What Ctrl+C / Ctrl+X last copied from the table (for Insert copied), with the text it put on the clipboard.</summary>
    private TableEdits.Clip? _clip;

    private static readonly Cursor RowSelectCursor = new(StandardCursorType.Hand);
    /// <summary>Width of the ⋮⋮ grip at the left of a row number: drag it to move the row(s), like the column grip.</summary>
    private const double RowGripWidth = 13;

    /// <summary>Row number content in edit mode: a faint ⋮⋮ grip (move) and the number (select).</summary>
    private static Control WithRowGrip(TextBlock number)
    {
        var grip = new Border
        {
            Width = RowGripWidth, Background = Brushes.Transparent, Cursor = RowDragCursor,
            Child = new TextBlock
            {
                Text = "⋮⋮", FontSize = 10, Opacity = 0.45, FontFamily = AppFonts.Thai,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        number.Margin = new Thickness(0, 0, 3, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RowGripWidth},*") };
        Grid.SetColumn(number, 1);
        grid.Children.Add(grip);
        grid.Children.Add(number);
        return grid;
    }

    // ───────────────────────── what is selected ─────────────────────────

    private int LastVisibleColumn()
    {
        var last = _session.Doc.Columns.Count - 1;
        while (last > 0 && ColumnGroupOps.IsHidden(_session.Doc, last)) last--;
        return Math.Max(0, last);
    }

    /// <summary>The block spans every column on screen (row numbers clicked, or a block as wide as the table).</summary>
    private bool WholeRowsSelected() =>
        SelectionRect() is { } s && s.Left <= FirstVisibleColumn() && s.Right >= LastVisibleColumn();

    /// <summary>The block spans every row on screen (a header clicked, or a block as tall as the table).</summary>
    private bool WholeColumnsSelected() =>
        SelectionRect() is { } s && _visibleRows.Count > 0 && s.Top <= _visibleRows[0] && s.Bottom >= _visibleRows[^1];

    private bool ColumnSelected(int column) => SelectionRect() is { } s && column >= s.Left && column <= s.Right;

    /// <summary>Rows a row command works on: the selected rows on screen when <paramref name="row"/> is one of them
    /// (or no row is given), otherwise just that row.</summary>
    private List<int> RowsFor(int row)
    {
        var rows = SelectedCells().Rows;
        if (row < 0) return rows;
        return rows.Contains(row) ? rows : row < _session.Doc.Rows.Count ? [row] : [];
    }

    /// <summary>Columns a column command works on: the selected columns when <paramref name="column"/> is one of them.</summary>
    private List<int> ColumnsFor(int column)
    {
        var cols = SelectedCells().Columns;
        if (column < 0) return cols;
        return cols.Contains(column) ? cols : column < _session.Doc.Columns.Count ? [column] : [];
    }

    /// <summary>Menu commands from the top Row menu work on whole rows: the rows of the selection that has
    /// <paramref name="row"/> become whole-row selected first.</summary>
    private void EnsureRowsSelected(int row)
    {
        var rows = RowsFor(row);
        if (rows.Count == 0 || (WholeRowsSelected() && rows.All(IsRowSelected))) return;
        SelectRows(rows[0], rows[^1]);
    }

    /// <summary>Same for the column menus: the columns of the selection that has <paramref name="column"/>, every row.</summary>
    private void EnsureColumnsSelected(int column)
    {
        var cols = ColumnsFor(column);
        if (cols.Count == 0 || (WholeColumnsSelected() && cols.All(ColumnSelected))) return;
        SelectColumns(cols[0], cols[^1], _menuHeaderBoxes.GetValueOrDefault(cols[0]));
    }

    // ───────────────────────── selecting whole rows / columns ─────────────────────────

    /// <summary>Rows <paramref name="anchorRow"/> … <paramref name="row"/>, every column; the anchor row's first cell is the active one.</summary>
    private void SelectRows(int anchorRow, int row, bool focus = true)
    {
        var doc = _session.Doc;
        if (doc.Rows.Count == 0) return;
        CommitFocusedEdit();
        anchorRow = Math.Clamp(anchorRow, 0, doc.Rows.Count - 1);
        row = Math.Clamp(row, 0, doc.Rows.Count - 1);
        var first = FirstVisibleColumn();
        _session.ChooseCell(anchorRow, first);
        _session.ExtendTo(row, LastVisibleColumn());
        if (focus) FocusCell(anchorRow, first, keepRange: true); // the row's first cell is the active one, as in Excel
        RefreshSelectionVisuals();
        UpdateStatus();
        var n = SelectedCells().Rows.Count;
        if (n > 1) StatusText.Text = Localizer.T("rows.selected", n);
    }

    /// <summary>Columns <paramref name="anchorCol"/> … <paramref name="column"/>, every row on screen. The keyboard goes to the
    /// header (so the rows do not jump to the top); Delete, Ctrl+C / X / V and the arrows work from there.</summary>
    private void SelectColumns(int anchorCol, int column, Control? focus = null)
    {
        var doc = _session.Doc;
        if (doc.Columns.Count == 0) return;
        CommitFocusedEdit();
        anchorCol = NearestVisibleGroupColumn(Math.Clamp(anchorCol, 0, doc.Columns.Count - 1));
        column = NearestVisibleGroupColumn(Math.Clamp(column, 0, doc.Columns.Count - 1));
        if (_visibleRows.Count == 0)
        {
            _session.ChooseCell(-1, anchorCol); // nothing to select; the column menu still knows the column
            _session.ExtendTo(-1, column);
        }
        else
        {
            _session.ChooseCell(_visibleRows[0], anchorCol);
            _session.ExtendTo(_visibleRows[^1], column);
        }
        if (focus != null && !focus.IsFocused) focus.Focus(NavigationMethod.Pointer);
        RefreshSelectionVisuals();
        UpdateStatus();
        var n = Math.Abs(column - anchorCol) + 1;
        if (n > 1) StatusText.Text = Localizer.T("cols.selected", SelectedCells().Columns.Count);
    }

    /// <summary>Row number pressed (PopulateRow). On the number: select the row, drag or Shift+click for several (Excel).
    /// On the ⋮⋮ grip: drag moves the row — or every selected row when it is one of them. Right: a selection that has
    /// this row is kept, so the menu works on all of them.</summary>
    private void OnRowLabelPressed(int row, Border label, PointerPressedEventArgs e)
    {
        if (_view != ViewMode.Edit) return;
        var point = e.GetCurrentPoint(label);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!(WholeRowsSelected() && IsRowSelected(row))) SelectRows(row, row);
            return; // the row menu opens
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && SelectionRect() != null)
        {
            SelectRows(_session.AnchorRow, row);
            return;
        }
        if (point.Position.X <= RowGripWidth + 1 && !_busy)
        {
            if (!(WholeRowsSelected() && IsRowSelected(row))) SelectRows(row, row);
            ArmRowMove(row, label, e); // the grip: a drag moves the selected rows; a click just selected the row
            return;
        }
        SelectRows(row, row);
        _dragKind = DragKind.Rows;
        StartDragSelect(e);
    }

    /// <summary>Column header text box: read-only (select mode) until a double-click or F2 renames the column.</summary>
    private void AttachExcelHeader(TextBox box, int column)
    {
        box.Classes.Add("colhead");
        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        box.Cursor = RowSelectCursor;
        ToolTip.SetTip(box, Localizer.T("cols.header_tip"));
        ToolTip.SetShowDelay(box, 1200); // clicked often: the tip waits for a real pause
        box.AddHandler(PointerPressedEvent, (_, e) => OnHeaderPressed(box, column, e), RoutingStrategies.Tunnel);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_view != ViewMode.Edit || box.IsReadOnly || e.Key != Key.Escape) return;
            e.Handled = true; // Esc: the name it had comes back
            if (box.Tag is EditCommit c && (box.Text ?? "") != c.Start) box.Text = c.Start;
            LeaveHeaderEdit(box);
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => LeaveHeaderEdit(box);
    }

    private void OnHeaderPressed(TextBox box, int column, PointerPressedEventArgs e)
    {
        if (_view != ViewMode.Edit || !box.IsReadOnly) return; // renaming: the box places the caret as usual
        var point = e.GetCurrentPoint(box);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!(WholeColumnsSelected() && ColumnSelected(column))) SelectColumns(column, column, box);
            return; // the column menu opens
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            EnterHeaderEdit(box);
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _session.HasSelection && _session.AnchorCol >= 0)
        {
            SelectColumns(_session.AnchorCol, column, box);
            return;
        }
        SelectColumns(column, column, box);
        _dragKind = DragKind.Columns;
        StartDragSelect(e);
    }

    /// <summary>Header focus without a click (Tab, the keyboard): that column is selected unless it already is.</summary>
    private void OnHeaderFocused(TextBox box, int column)
    {
        if (_view != ViewMode.Edit || !box.IsReadOnly) return;
        if (WholeColumnsSelected() && ColumnSelected(column)) { RefreshSelectionVisuals(); return; }
        SelectColumns(column, column);
    }

    private void EnterHeaderEdit(TextBox box)
    {
        if (_view != ViewMode.Edit) return;
        box.IsReadOnly = false;
        box.ClearValue(TextBox.CaretBrushProperty);
        box.Cursor = new Cursor(StandardCursorType.Ibeam);
        if (!box.IsFocused) box.Focus();
        box.SelectAll(); // like renaming a file: type the new name, or click to place the caret
        StatusText.Text = Localizer.T("cols.renaming");
    }

    private void LeaveHeaderEdit(TextBox box)
    {
        if (box.IsReadOnly) return;
        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        box.Cursor = RowSelectCursor;
        box.SelectionStart = box.SelectionEnd = 0;
    }

    /// <summary>The focused column header, when it is in select mode (keys then work on the selected columns).</summary>
    private TextBox? FocusedHeaderBox() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: true } box && box.Classes.Contains("colhead") ? box : null;

    /// <summary>Header fill of the selected columns (when whole columns are selected), row-number cursors.</summary>
    private void RefreshHeaderVisuals()
    {
        var whole = _view == ViewMode.Edit && WholeColumnsSelected() && !WholeRowsSelected();
        foreach (var (column, box) in _menuHeaderBoxes)
            box.Classes.Set("selected", whole && ColumnSelected(column));
    }

    // ───────────────────────── moving the selected rows ─────────────────────────

    private void ArmRowMove(int row, Border label, PointerPressedEventArgs e)
    {
        _rowDragRow = row;
        _rowDragging = false;
        _rowDragStart = e.GetPosition(RowsList);
        e.Pointer.Capture(label);
    }

    /// <summary>A drag of selected row numbers ended on a gap of the visible list: one row moves as before (RowMoveAction);
    /// several move together and keep their order (RowOrderAction). One undo step.</summary>
    private void DropSelectedRows(int pressedRow, int slot)
    {
        var rows = RowsFor(pressedRow);
        if (rows.Count <= 1)
        {
            var target = RowOps.TargetForSlot(_visibleRows, pressedRow, slot);
            if (target >= 0) _ = RunStructureAsync(() => { MoveRowTo(pressedRow, target, focus: false); return Task.CompletedTask; });
            return;
        }
        _ = RunStructureAsync(() =>
        {
            var doc = _session.Doc;
            var before = doc.RowIds.ToList();
            var ids = rows.Select(doc.RowIdAt).ToList();
            if (!RowOps.MoveRowsToSlot(doc, _visibleRows, rows, slot)) return Task.CompletedTask;
            _undo.Push(new RowOrderAction(before, doc.RowIds.ToList(), Localizer.T("undo.move_rows")));
            Rebuild();
            var first = doc.RowIds.IndexOf(ids[0]);
            var last = doc.RowIds.IndexOf(ids[^1]);
            if (first >= 0 && last >= 0) SelectRows(first, last);
            FlushSave();
            StatusText.Text = Localizer.T("rows.moved_block", rows.Count);
            return Task.CompletedTask;
        });
    }

    // ───────────────────────── insert / delete ─────────────────────────

    /// <summary>Inserts as many blank rows as are selected (or one), above the first or below the last; they become the selection.</summary>
    private Task InsertRowsAsync(int row, bool above) => RunStructureAsync(() => SnapshotStep(Localizer.T("undo.insert_rows"), () =>
    {
        var doc = _session.Doc;
        var rows = RowsFor(row);
        var count = Math.Max(1, rows.Count);
        var index = rows.Count == 0 ? (above ? 0 : doc.Rows.Count) : above ? rows[0] : rows[^1] + 1;
        if (!TableEdits.InsertRows(doc, index, count)) { StatusText.Text = TableLimits.RowLimitMessage; return Task.CompletedTask; }
        Rebuild();
        SelectRows(index, index + count - 1);
        FlushSave();
        StatusText.Text = Localizer.T("rows.inserted", count);
        return Task.CompletedTask;
    }));

    /// <summary>Deletes the selected rows on screen (or the given row). Asks first when any of them has text. One row
    /// (or none selected: the last row, as F6 always did) goes the one-row way.</summary>
    private async Task DeleteRowsAsync(int row)
    {
        var doc = _session.Doc;
        var rows = RowsFor(row);
        if (row < 0 && rows.Count > 1 && WholeColumnsSelected() && !WholeRowsSelected())
        {
            StatusText.Text = Localizer.T("rows.pick_rows_first"); // F6 with a column selected would take every row
            return;
        }
        if (rows.Count <= 1)
        {
            if (rows.Count == 1) _session.ChooseCell(rows[0], Math.Max(0, _session.AnchorCol)); // the one-row delete works on the active row
            await DeleteRowAsync();
            return;
        }
        var ids = rows.Select(doc.RowIdAt).ToList();
        _busy = true;
        try
        {
            if (rows.Any(doc.RowHasContent) && !await _confirm.ConfirmAsync(Localizer.T("rows.delete_confirm", rows.Count)))
            {
                StatusText.Text = Localizer.T("delete.canceled");
                return;
            }
        }
        finally { _busy = false; }
        await SnapshotStep(Localizer.T("undo.delete_rows"), () =>
        {
            var now = ids.Select(id => doc.RowIds.IndexOf(id)).Where(i => i >= 0).ToList(); // rows may have moved meanwhile
            var top = now.Count > 0 ? now.Min() : 0;
            var n = TableEdits.RemoveRows(doc, now);
            Rebuild();
            if (_visibleRows.Count > 0)
            {
                var next = _visibleRows.FirstOrDefault(r => r >= top, _visibleRows[^1]);
                _session.ChooseCell(next, FirstVisibleColumn());
                FocusCell(next, FirstVisibleColumn());
            }
            else _session.ClearSelection();
            FlushSave();
            StatusText.Text = Localizer.T("rows.deleted", n);
            return Task.CompletedTask;
        });
    }

    /// <summary>Inserts as many new columns as are selected (or one), left of the first or right of the last.</summary>
    private Task InsertColumnsAsync(int column, bool left) => RunStructureAsync(() => SnapshotStep(Localizer.T("undo.insert_columns"), () =>
    {
        var doc = _session.Doc;
        var cols = ColumnsFor(column);
        var count = Math.Max(1, cols.Count);
        var index = cols.Count == 0 ? doc.Columns.Count : left ? cols[0] : cols[^1] + 1;
        while (!left && index < doc.Columns.Count && ColumnGroupOps.IsHidden(doc, index)) index++; // past a collapsed group's children
        if (TableEdits.InsertColumns(doc, index, count) is not { } ids) { StatusText.Text = TableLimits.ColumnLimitMessage; return Task.CompletedTask; }
        Rebuild();
        SelectColumns(doc.ColumnIds.IndexOf(ids[0]), doc.ColumnIds.IndexOf(ids[^1]));
        FlushSave();
        StatusText.Text = Localizer.T("cols.inserted", ids.Count);
        return Task.CompletedTask;
    }));

    /// <summary>Deletes the selected columns (or the given one). One column keeps its own path (status / reminder column
    /// messages; F8 without a header still asks only when it has text); several are confirmed once, by name.</summary>
    private async Task DeleteColumnsAsync(int column)
    {
        var doc = _session.Doc;
        var cols = ColumnsFor(column);
        if (cols.Count <= 1)
        {
            if (column < 0) await SnapshotStep(Localizer.T("delete.undo_column"), DeleteColumnAsync);
            else await SnapshotStep(Localizer.T("delete.undo_column"), () => DeleteColumnAtAsync(cols.Count == 1 ? cols[0] : column));
            return;
        }
        if (cols.Count >= doc.Columns.Count) { StatusText.Text = Localizer.T("delete.min_one_column"); return; }
        doc.EnsureColumnIds();
        var ids = cols.Select(c => doc.ColumnIds[c]).ToList();
        var names = string.Join(", ", cols.Select(c => DeletePlanner.ColumnLabel(doc, c)));
        _busy = true;
        try
        {
            if (!await _confirm.ConfirmAsync(Localizer.T("cols.delete_confirm", cols.Count, names)))
            {
                StatusText.Text = Localizer.T("delete.canceled");
                return;
            }
        }
        finally { _busy = false; }
        await SnapshotStep(Localizer.T("undo.delete_columns"), () =>
        {
            var now = ids.Select(id => doc.ColumnIds.IndexOf(id)).Where(i => i >= 0).ToList();
            var left = now.Count > 0 ? now.Min() : 0;
            var n = TableEdits.RemoveColumns(doc, now);
            _session.ChooseCell(_visibleRows.Count > 0 ? _visibleRows[0] : -1, Math.Clamp(left, 0, doc.Columns.Count - 1));
            Rebuild();
            FlushSave();
            AfterColumnRemoved();
            StatusText.Text = Localizer.T("cols.deleted", n);
            return Task.CompletedTask;
        });
    }

    private enum InsertAs { Rows, Columns, Either }

    /// <summary>Insert copied: rows (above the selected rows), columns (left of the selected columns), or — from a cell —
    /// whichever was copied. A copy made in this table brings its row / column data; other text goes in as new rows.</summary>
    private async Task InsertCopiedAsync(InsertAs kind, int row, int column)
    {
        if (_view != ViewMode.Edit || _busy) return;
        if (Clipboard == null) { StatusText.Text = Localizer.T("ui.paste_failed"); return; }
        string? text;
        try { text = await Clipboard.TryGetTextAsync(); }
        catch (Exception ex) { StatusText.Text = Localizer.T("ui.paste_failed_reason", ex.Message); return; }
        if (string.IsNullOrEmpty(text)) { StatusText.Text = Localizer.T("insert.nothing_copied"); return; }
        var clip = _clip != null && _clip.Matches(text) ? _clip : null;
        var asColumns = kind == InsertAs.Columns || (kind == InsertAs.Either && clip?.Columns != null);
        if (asColumns && clip?.Rows != null) { StatusText.Text = Localizer.T("insert.copied_rows_here"); return; }
        if (!asColumns && clip?.Columns != null) { StatusText.Text = Localizer.T("insert.copied_columns_here"); return; }
        CommitFocusedEdit();
        var doc = _session.Doc;
        if (asColumns)
        {
            var cols = ColumnsFor(column);
            var index = cols.Count > 0 ? cols[0] : Math.Clamp(column, 0, doc.Columns.Count);
            await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.insert_copied"), () =>
            {
                var countBefore = doc.Columns.Count;
                string? error = null;
                if (clip?.Columns != null) { if (TableEdits.InsertColumnCopies(doc, index, clip.Columns) == 0) error = TableLimits.ColumnLimitMessage; }
                else error = TableEdits.InsertTextColumns(doc, index, _visibleRows, text);
                if (error != null) { StatusText.Text = error; return Task.CompletedTask; }
                var added = doc.Columns.Count - countBefore;
                Rebuild();
                SelectColumns(index, index + added - 1);
                FlushSave();
                StatusText.Text = Localizer.T("cols.copied_inserted", added);
                return Task.CompletedTask;
            }));
            return;
        }
        var rows = RowsFor(row);
        var at = rows.Count > 0 ? rows[0] : Math.Clamp(row, 0, doc.Rows.Count);
        var from = WholeRowsSelected() || SelectionRect() is null ? FirstVisibleColumn() : Math.Max(0, SelectionRect()!.Value.Left);
        await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.insert_copied"), () =>
        {
            var countBefore = doc.Rows.Count;
            string? error = null;
            if (clip?.Rows != null) { if (!TableEdits.InsertRowCopies(doc, at, clip.Rows)) error = TableLimits.RowLimitMessage; }
            else error = TableEdits.InsertTextRows(doc, at, from, text);
            if (error != null) { StatusText.Text = error; return Task.CompletedTask; }
            var added = doc.Rows.Count - countBefore;
            Rebuild();
            SelectRows(at, at + added - 1);
            FlushSave();
            _reminders.CheckNow();
            StatusText.Text = Localizer.T("rows.copied_inserted", added);
            return Task.CompletedTask;
        }));
    }

    /// <summary>Called when Ctrl+C / Ctrl+X put the selection on the clipboard: whole rows or columns are remembered
    /// with their data for Insert copied.</summary>
    private void RememberCopy(string text)
    {
        var (rows, cols) = SelectedCells();
        var doc = _session.Doc;
        _clip = WholeRowsSelected() ? TableEdits.CopyRows(doc, rows, text)
            : WholeColumnsSelected() ? TableEdits.CopyColumns(doc, cols, rows, text)
            : new TableEdits.Clip { Text = text };
    }

    // ───────────────────────── menu items ─────────────────────────

    private static string CountLabel(string key, int n) => n == 1 ? Localizer.T(key + "_one") : Localizer.T(key, n);

    /// <summary>Insert / delete / insert copied for the row menus (row number, top Row menu).</summary>
    private List<Control> RowEditMenuItems(int row)
    {
        var edit = _view == ViewMode.Edit && !_busy;
        var n = Math.Max(1, RowsFor(row).Count);
        var has = row >= 0 && row < _session.Doc.Rows.Count;
        MenuItem Item(string header, bool enabled, Action act, KeyGesture? g = null)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled, InputGesture = g, FontFamily = AppFonts.Thai };
            m.Click += (_, _) => act();
            return m;
        }
        return
        [
            Item(Localizer.T("cell.menu_copy"), edit && has, () => { EnsureRowsSelected(row); _ = CopySelectionAsync(); }, new KeyGesture(Key.C, KeyModifiers.Control)),
            Item(Localizer.T("cell.menu_cut"), edit && has, () => { EnsureRowsSelected(row); _ = CutAsync(); }, new KeyGesture(Key.X, KeyModifiers.Control)),
            Item(Localizer.T("cell.menu_paste"), edit && has, () => { EnsureRowsSelected(row); _ = PasteIntoSelectionAsync(); }, new KeyGesture(Key.V, KeyModifiers.Control)),
            Item(Localizer.T("rows.insert_copied"), edit, () => _ = InsertCopiedAsync(InsertAs.Rows, row, -1)),
            new Separator(),
            Item(CountLabel("rows.insert_above", n), edit, () => _ = InsertRowsAsync(row, above: true)),
            Item(CountLabel("rows.insert_below", n), edit, () => _ = InsertRowsAsync(row, above: false)),
            Item(CountLabel("rows.delete", n), edit && has, () => _ = RunStructureAsync(() => DeleteRowsAsync(row)), new KeyGesture(Key.F6)),
            Item(Localizer.T("cell.menu_clear"), edit && has, () => { EnsureRowsSelected(row); ClearSelectedCells(Localizer.T("undo.clear_cells")); }, new KeyGesture(Key.Delete)),
            new Separator()
        ];
    }

    /// <summary>Insert / delete / insert copied for the column menus (header, top Column menu).</summary>
    private List<Control> ColumnEditMenuItems(int column)
    {
        var edit = _view == ViewMode.Edit && !_busy;
        var has = column >= 0 && column < _session.Doc.Columns.Count;
        var n = Math.Max(1, ColumnsFor(column).Count);
        MenuItem Item(string header, bool enabled, Action act)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled, FontFamily = AppFonts.Thai };
            m.Click += (_, _) => act();
            return m;
        }
        return
        [
            Item(Localizer.T("cols.insert_copied"), edit, () => _ = InsertCopiedAsync(InsertAs.Columns, -1, column)),
            new Separator(),
            Item(CountLabel("cols.insert_left", n), edit, () => _ = InsertColumnsAsync(column, left: true)),
            Item(CountLabel("cols.insert_right", n), edit, () => _ = InsertColumnsAsync(column, left: false)),
            Item(Localizer.T("cell.menu_clear"), edit && has && _visibleRows.Count > 0, () => { EnsureColumnsSelected(column); ClearSelectedCells(Localizer.T("undo.clear_cells")); })
        ];
    }

    /// <summary>Cell menu: Insert ▸ rows above / columns left, Delete ▸ rows / columns, Insert copied cells.</summary>
    private List<Control> CellStructureMenuItems(int row, int column)
    {
        var edit = _view == ViewMode.Edit && !_busy;
        var rows = Math.Max(1, RowsFor(row).Count);
        var cols = Math.Max(1, ColumnsFor(column).Count);
        MenuItem Item(string header, bool enabled, Action act)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled, FontFamily = AppFonts.Thai };
            m.Click += (_, _) => act();
            return m;
        }
        var insert = new MenuItem { Header = Localizer.T("cell.menu_insert"), IsEnabled = edit, FontFamily = AppFonts.Thai };
        insert.Items.Add(Item(CountLabel("rows.insert_above", rows), edit, () => _ = InsertRowsAsync(row, above: true)));
        insert.Items.Add(Item(CountLabel("rows.insert_below", rows), edit, () => _ = InsertRowsAsync(row, above: false)));
        insert.Items.Add(Item(CountLabel("cols.insert_left", cols), edit, () => _ = InsertColumnsAsync(column, left: true)));
        insert.Items.Add(Item(CountLabel("cols.insert_right", cols), edit, () => _ = InsertColumnsAsync(column, left: false)));
        var delete = new MenuItem { Header = Localizer.T("cell.menu_delete"), IsEnabled = edit, FontFamily = AppFonts.Thai };
        delete.Items.Add(Item(CountLabel("rows.delete", rows), edit, () => _ = RunStructureAsync(() => DeleteRowsAsync(row))));
        delete.Items.Add(Item(CountLabel("cols.delete", cols), edit, () => _ = RunStructureAsync(() => DeleteColumnsAsync(column))));
        return
        [
            Item(Localizer.T("cell.menu_insert_copied"), edit, () => _ = InsertCopiedAsync(InsertAs.Either, row, column)),
            insert,
            delete
        ];
    }

    /// <summary>Right-click on a cell that is not a text box (status, date, check, choice, days): the cell menu.</summary>
    private void AttachCellMenu(Control control, int row, int column)
    {
        control.AddHandler(ContextRequestedEvent, (_, e) =>
        {
            if (e.Handled || _view != ViewMode.Edit) return;
            e.Handled = true;
            if (!InSelection(row, column)) { CommitFocusedEdit(); _session.ChooseCell(row, column); RefreshSelectionVisuals(); }
            ShowCellMenu(control, null, row, column);
        }, RoutingStrategies.Bubble);
    }
}
