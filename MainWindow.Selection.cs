using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>
/// 2.6.0: Excel-style cells. A click selects a cell (gold fill) instead of putting a caret in it; dragging selects a
/// block; typing replaces the cell, double-click or F2 edits the text that is there (Esc puts it back); arrows, Tab and
/// Enter move, Shift extends; Delete clears, Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste blocks (tab-separated, like
/// Excel); Ctrl+A or the top-left corner selects everything; a click on an empty spot clears the selection.
/// There is no fill handle. The selection is (AnchorRow, AnchorCol) = the active cell, which has the focus, to
/// (ActiveRow, ActiveCol) = the moving corner, in document rows; operations touch the rows that are on screen.
/// </summary>
public partial class MainWindow
{
    /// <summary>Drawn over the cells of a row (never hit-tested): selection, active cell, find matches.</summary>
    private sealed class CellOverlay : Border
    {
        public CellOverlay() => IsHitTestVisible = false;
    }

    /// <summary>Over cells that are not text boxes (status, date, check, days); text cells use the "selected" style (MainWindow.axaml).</summary>
    private static readonly IBrush SelectionFill = new SolidColorBrush(Color.FromArgb(0x55, 0xE0, 0xA8, 0x2E));
    private static readonly IBrush SelectionEdge = new SolidColorBrush(Color.Parse("#C08A1E"));
    private static readonly IBrush ActiveEdge = new SolidColorBrush(Color.Parse("#7A4E00"));
    private static readonly IBrush MatchFill = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xD2, 0x3C));
    private static readonly IBrush CurrentMatchEdge = new SolidColorBrush(Color.Parse("#E06A00"));

    private bool _dragSelecting;
    private MenuFlyout? _lastCellMenu;
    private Point _dragPoint;
    private DispatcherTimer? _dragScrollTimer;
    /// <summary>The next cell focus is the active cell of a block: keep the block (normally a focus selects that one cell).</summary>
    private bool _keepRange;

    /// <summary>Selected block in document coordinates (inclusive), or null (nothing, or a header).</summary>
    private (int Top, int Left, int Bottom, int Right)? SelectionRect()
    {
        var doc = _session.Doc;
        if (!_session.HasSelection || doc.Rows.Count == 0 || doc.Columns.Count == 0) return null;
        if (_session.AnchorRow < 0 || _session.ActiveRow < 0 || _session.AnchorCol < 0 || _session.ActiveCol < 0) return null;
        int R(int r) => Math.Clamp(r, 0, doc.Rows.Count - 1);
        int C(int c) => Math.Clamp(c, 0, doc.Columns.Count - 1);
        int r1 = R(_session.AnchorRow), r2 = R(_session.ActiveRow), c1 = C(_session.AnchorCol), c2 = C(_session.ActiveCol);
        return (Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
    }

    private bool InSelection(int row, int column) =>
        SelectionRect() is { } s && row >= s.Top && row <= s.Bottom && column >= s.Left && column <= s.Right;

    private bool IsRowSelected(int row) => SelectionRect() is { } s && row >= s.Top && row <= s.Bottom;

    /// <summary>On-screen rows and columns of the block, in order (hidden done rows and collapsed columns left out).</summary>
    private (List<int> Rows, List<int> Columns) SelectedCells()
    {
        if (SelectionRect() is not { } s) return ([], []);
        var rows = _visibleRows.Where(r => r >= s.Top && r <= s.Bottom).ToList();
        var cols = Enumerable.Range(s.Left, s.Right - s.Left + 1).Where(c => !ColumnGroupOps.IsHidden(_session.Doc, c)).ToList();
        return (rows, cols);
    }

    private int SelectedCellCount() { var (r, c) = SelectedCells(); return r.Count * c.Count; }

    /// <summary>Repaints the selection, find marks and row numbers of the rows on screen; the formula bar follows.</summary>
    private void RefreshSelectionVisuals()
    {
        foreach (var c in RowsList.GetRealizedContainers())
        {
            if (c is not ContentPresenter { Child: Grid { Tag: int r } g } || r >= _session.Doc.Rows.Count) continue;
            if (g.Children.FirstOrDefault() is Border label)
            {
                label.Background = LabelBrushFor(r, RowNumberBackground(r));
            }
            ApplyOverlays(g, r);
        }
        RefreshHeaderVisuals();
        SyncFormulaBar();
    }

    /// <summary>Overlays of one row grid: find matches, the selected block, the active cell.</summary>
    private void ApplyOverlays(Grid grid, int row)
    {
        for (var i = grid.Children.Count - 1; i >= 0; i--)
            if (grid.Children[i] is CellOverlay) grid.Children.RemoveAt(i);
        if (_view != ViewMode.Edit) return;
        var columns = _session.Doc.Columns.Count;

        // Find marks: a text cell gets the "match" class (yellow behind its text), other cells a yellow overlay;
        // the current match has an orange frame.
        var matchCols = new HashSet<int>();
        for (var i = 0; i < _findMatches.Count; i++)
        {
            var (mr, mc) = _findMatches[i];
            if (mr != row || mc >= columns) continue;
            matchCols.Add(mc);
            if (i != _findIndex) continue;
            var frame = new CellOverlay { BorderBrush = CurrentMatchEdge, BorderThickness = new Thickness(2) };
            Grid.SetColumn(frame, mc + 1);
            grid.Children.Add(frame);
        }

        var sel = SelectionRect();
        var inBlock = sel is { } b0 && row >= b0.Top && row <= b0.Bottom;
        var editingBox = FocusedCellBox() is { IsReadOnly: false } eb ? eb : null;
        // Fill: a text cell gets the "selected" class (its own background, behind the text); other cells an overlay.
        foreach (var child in grid.Children.ToList())
        {
            if (child is CellOverlay) continue;
            var col = Grid.GetColumn(child) - 1;
            if (col < 0 || col >= columns) continue;
            var box = child as TextBox ?? child.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(IsDataCell);
            var selected = inBlock && col >= sel!.Value.Left && col <= sel.Value.Right && !ReferenceEquals(box, editingBox);
            var match = matchCols.Contains(col) && !selected;
            if (box != null && IsDataCell(box))
            {
                box.Classes.Set("selected", selected);
                box.Classes.Set("match", match);
                continue;
            }
            if (!selected && !match) continue;
            var fill = new CellOverlay { Background = selected ? SelectionFill : MatchFill };
            Grid.SetColumn(fill, col + 1);
            grid.Children.Add(fill);
        }
        if (sel is not { } s || !inBlock) return;
        var single = s.Top == s.Bottom && s.Left == s.Right;
        if (!single)
        {
            const double edge = 1.5;
            var block = new CellOverlay
            {
                BorderBrush = SelectionEdge,
                BorderThickness = new Thickness(edge, row == s.Top ? edge : 0, edge, row == s.Bottom ? edge : 0)
            };
            Grid.SetColumn(block, s.Left + 1);
            Grid.SetColumnSpan(block, s.Right - s.Left + 1);
            grid.Children.Add(block);
        }
        if (row == _session.AnchorRow && _session.AnchorCol >= 0 && _session.AnchorCol < columns)
        {
            var active = new CellOverlay { BorderBrush = ActiveEdge, BorderThickness = new Thickness(2) };
            Grid.SetColumn(active, _session.AnchorCol + 1);
            grid.Children.Add(active);
        }
    }

    /// <summary>The focused data cell's text box, if a data cell has the focus.</summary>
    private TextBox? FocusedCellBox() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Tag: EditCommit } box && IsDataCell(box) ? box : null;

    private static bool IsDataCell(Control control) => control.Classes.Contains("cell");

    /// <summary>The element has the focus inside a table row (a cell control, or a control inside a cell).</summary>
    private bool FocusInRows()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual v) return false;
        return v.GetVisualAncestors().Prepend(v).Any(a => a is Grid { Tag: int } && a.GetVisualParent() is ContentPresenter);
    }

    /// <summary>A cell has the focus, or a column header in select mode (the selected columns take the keys).</summary>
    private bool FocusInTable() => FocusInRows() || FocusedHeaderBox() != null;

    // ───────────────────────── text cells: select mode ↔ edit mode ─────────────────────────

    /// <summary>Data text cells start in select mode: read-only, no caret. Called from MakeTextBox.</summary>
    private void AttachExcelCell(TextBox box, int row, int column)
    {
        box.Classes.Add("cell");
        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        box.ContextFlyout = null;
        box.ContextRequested += (_, e) => { e.Handled = true; ShowCellMenu(box, box, row, column); };
        box.AddHandler(PointerPressedEvent, (_, e) => OnCellPointerPressed(box, row, column, e), RoutingStrategies.Tunnel);
        box.AddHandler(TextInputEvent, (_, e) =>
        {
            if (_view != ViewMode.Edit || !box.IsReadOnly || string.IsNullOrEmpty(e.Text)) return;
            if (e.Text.Any(char.IsControl)) return;
            e.Handled = true;
            CollapseTo(row, column);
            EnterEditMode(box, e.Text); // Excel: typing replaces what is there
        }, RoutingStrategies.Tunnel);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled || _view != ViewMode.Edit || box.IsReadOnly) return;
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CancelEdit(box);
                    break;
                case Key.Tab:
                    e.Handled = true;
                    (box.Tag as EditCommit)?.Commit(box);
                    MoveBy(row, column, 0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    break;
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => LeaveEditMode(box);
    }

    private void EnterEditMode(TextBox box, string? replaceWith)
    {
        if (_view != ViewMode.Edit) return;
        if (box.IsReadOnly)
        {
            box.IsReadOnly = false;
            box.ClearValue(TextBox.CaretBrushProperty);
        }
        if (replaceWith != null) box.Text = replaceWith; // TextChanged writes the cell; the undo step comes on commit
        var end = (box.Text ?? "").Length;
        box.SelectionStart = box.SelectionEnd = end;
        box.CaretIndex = end;
        if (!box.IsFocused) box.Focus();
        RefreshSelectionVisuals();
    }

    private void LeaveEditMode(TextBox box)
    {
        if (box.IsReadOnly) return;
        box.IsReadOnly = true;
        box.CaretBrush = Brushes.Transparent;
        box.SelectionStart = box.SelectionEnd = 0;
        if (FindOpen) RefreshFindMatches(); // the find marks follow the edit
        RefreshSelectionVisuals();
    }

    /// <summary>Esc while editing: the text the cell had when editing started comes back.</summary>
    private void CancelEdit(TextBox box)
    {
        if (box.Tag is EditCommit commit && (box.Text ?? "") != commit.Start) box.Text = commit.Start;
        LeaveEditMode(box);
        StatusText.Text = Localizer.T("cell.edit_cancelled");
    }

    private void OnCellPointerPressed(TextBox box, int row, int column, PointerPressedEventArgs e)
    {
        if (_view != ViewMode.Edit) return;
        if (!box.IsReadOnly) return; // editing: the text box places the caret and selects text as usual
        var point = e.GetCurrentPoint(box);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!InSelection(row, column)) SelectCell(row, column, box);
            return; // the cell menu opens (ContextRequested)
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            CollapseTo(row, column);
            EnterEditMode(box, null); // double-click: edit the text that is there
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && SelectionRect() != null)
        {
            CommitFocusedEdit();
            _session.ExtendTo(row, column); // the active cell keeps the focus, like Excel
            RefreshSelectionVisuals();
        }
        else SelectCell(row, column, box);
        StartDragSelect(e);
    }

    /// <summary>Cells that are not text boxes (status / choice lists, check boxes, the reminder date editor): a click or the
    /// focus selects the cell, Shift+click extends the block; their own controls work as before.</summary>
    private void AttachTypedCell(Control root, int row, int column)
    {
        root.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_view != ViewMode.Edit || !e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && SelectionRect() != null) _session.ExtendTo(row, column);
            else if (!InSelectionAsActive(row, column)) { CommitFocusedEdit(); _session.ChooseCell(row, column); }
            RefreshSelectionVisuals();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(GotFocusEvent, (_, e) => { if (_view == ViewMode.Edit) OnCellFocused(row, column, e.KeyModifiers); },
            RoutingStrategies.Bubble);
        AttachCellMenu(root, row, column); // 2.6.0: right-click → copy, insert, delete, like a text cell
    }

    /// <summary>One cell, focused, in select mode.</summary>
    private void SelectCell(int row, int column, Control? control = null)
    {
        CommitFocusedEdit();
        _session.ChooseCell(row, column);
        if (control != null && !control.IsFocused) control.Focus(NavigationMethod.Pointer);
        RefreshSelectionVisuals();
    }

    /// <summary>The block shrinks to its active cell (typing / double-click on a cell of a block).</summary>
    private void CollapseTo(int row, int column)
    {
        if (_session.AnchorRow == row && _session.AnchorCol == column && _session.ActiveRow == row && _session.ActiveCol == column) return;
        _session.ChooseCell(row, column);
        RefreshSelectionVisuals();
    }

    /// <summary>Cell focus handlers call this: a plain focus selects that cell; Shift (or a block being kept) does not.</summary>
    private void OnCellFocused(int row, int column, KeyModifiers modifiers)
    {
        if (_view == ViewMode.Background) return;
        if (_keepRange) _keepRange = false;
        else if (!modifiers.HasFlag(KeyModifiers.Shift) && !InSelectionAsActive(row, column)) _session.ChooseCell(row, column);
        RefreshSelectionVisuals();
        UpdateStatus();
    }

    /// <summary>The cell is already the active cell of the block (focus came back to it): keep the block.</summary>
    private bool InSelectionAsActive(int row, int column) =>
        _session.HasSelection && _session.AnchorRow == row && _session.AnchorCol == column && InSelection(row, column);

    // ───────────────────────── dragging a block ─────────────────────────

    private void InitExcelSelection()
    {
        TableHost.AddHandler(PointerMovedEvent, OnTablePointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        TableHost.AddHandler(PointerReleasedEvent, OnTablePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        TableHost.AddHandler(PointerCaptureLostEvent, (_, _) => StopDragSelect(), RoutingStrategies.Bubble, handledEventsToo: true);
        // A click that nothing else took (empty table area, margins, status line): the selection goes.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Handled || _view != ViewMode.Edit || !_session.HasSelection) return;
            if (e.Source is Visual v && v.GetVisualAncestors().Prepend(v).Any(a => ReferenceEquals(a, FormulaBar) || ReferenceEquals(a, FindBar)))
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            ClearCellSelection();
        }, RoutingStrategies.Bubble);
    }

    private void StartDragSelect(PointerPressedEventArgs e)
    {
        _dragSelecting = true;
        _dragPoint = e.GetPosition(TableHost);
        e.Pointer.Capture(TableHost);
        _dragScrollTimer ??= NewDragScrollTimer();
        if (_dragKind != DragKind.Columns) _dragScrollTimer.Start(); // headers: no row scrolling
    }

    private void StopDragSelect()
    {
        _dragSelecting = false;
        _dragKind = DragKind.Cells;
        _dragScrollTimer?.Stop();
    }

    private void OnTablePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragSelecting) return;
        _dragPoint = e.GetPosition(TableHost);
        ExtendDragTo(_dragPoint);
    }

    private void OnTablePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragSelecting) return;
        StopDragSelect();
        e.Pointer.Capture(null);
    }

    private void ExtendDragTo(Point point)
    {
        if (_dragKind == DragKind.Columns)
        {
            // Headers: the column under the pointer, every row (2.6.0).
            if (TableHost.TranslatePoint(point, TableGrid) is not { } p) return;
            var column = NearestVisibleGroupColumn(ColumnAtX(p.X));
            if (column == _session.ActiveCol) return;
            _session.ExtendTo(_session.ActiveRow, column);
            RefreshSelectionVisuals();
            UpdateDragCount();
            return;
        }
        if (CellAt(point) is not { } hit) return;
        if (_dragKind == DragKind.Rows) hit = (hit.Row, LastVisibleColumn()); // row numbers: whole rows
        if (hit.Row == _session.ActiveRow && hit.Column == _session.ActiveCol) return;
        _session.ExtendTo(hit.Row, hit.Column);
        RefreshSelectionVisuals();
        UpdateDragCount();
    }

    private void UpdateDragCount()
    {
        var (rows, cols) = SelectedCells();
        if (_dragKind == DragKind.Rows && rows.Count > 1) StatusText.Text = Localizer.T("rows.selected", rows.Count);
        else if (_dragKind == DragKind.Columns && cols.Count > 1) StatusText.Text = Localizer.T("cols.selected", cols.Count);
    }

    /// <summary>The cell (document row, column) under a point of the table, or null.</summary>
    private (int Row, int Column)? CellAt(Point point)
    {
        var hit = TableHost.InputHitTest(point) as Visual;
        for (var v = hit; v != null; v = v.GetVisualParent())
        {
            if (v.GetVisualParent() is Grid { Tag: int row } g && g.GetVisualParent() is ContentPresenter && v is Control child)
            {
                var col = Grid.GetColumn(child) - 1;
                if (col < 0) col = 0; // the row number: first column
                return (row, NearestVisibleGroupColumn(col));
            }
        }
        return null;
    }

    /// <summary>While dragging past the top or bottom of the rows, the rows scroll and the block follows.</summary>
    private DispatcherTimer NewDragScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (!_dragSelecting) { timer.Stop(); return; }
            if (RowsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } sv) return;
            if (TableHost.TranslatePoint(_dragPoint, sv) is not { } p) return;
            double dy = p.Y < 4 ? -24 : p.Y > sv.Bounds.Height - 4 ? 24 : 0;
            if (dy == 0) return;
            var max = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            sv.Offset = new Vector(sv.Offset.X, Math.Clamp(sv.Offset.Y + dy, 0, max));
            sv.UpdateLayout();
            // The row now at that edge joins the block.
            var edge = new Point(Math.Clamp(p.X, 1, Math.Max(1, sv.Bounds.Width - 1)), dy < 0 ? 6 : sv.Bounds.Height - 6);
            if (sv.TranslatePoint(edge, TableHost) is { } q) ExtendDragTo(q);
        };
        return timer;
    }

    // ───────────────────────── keyboard ─────────────────────────

    /// <summary>Window-level keys for a selected cell that is not being edited (any cell type). True when handled.</summary>
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        if (_view != ViewMode.Edit || !FocusInTable()) return false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox { IsReadOnly: false }) return false; // editing text: caret keys, the box's own copy/paste
        if (focused is ComboBox { IsDropDownOpen: true }) return false;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false; // Alt+Shift+arrows move rows / columns
        if (!_session.HasSelection || _session.AnchorRow < 0)
        {
            if (e.Key == Key.F2 && FocusedHeaderBox() is { } h) { EnterHeaderEdit(h); return true; } // empty table: still renames
            return false;
        }
        int row = _session.AnchorRow, col = _session.AnchorCol;
        switch (e.Key)
        {
            case Key.Up: case Key.Down: case Key.Left: case Key.Right:
            {
                var (dr, dc) = e.Key switch { Key.Up => (-1, 0), Key.Down => (1, 0), Key.Left => (0, -1), _ => (0, 1) };
                if (shift) ExtendBy(dr, dc);
                else MoveBy(row, col, dr, dc);
                return true;
            }
            case Key.Tab:
                MoveBy(row, col, 0, shift ? -1 : 1);
                return true;
            case Key.Home when !ctrl:
                FocusCell(row, FirstVisibleColumn());
                return true;
            case Key.F2:
                if (focused is TextBox box && IsDataCell(box)) { CollapseTo(row, col); EnterEditMode(box, null); }
                else if (FocusedHeaderBox() is { } header) EnterHeaderEdit(header); // 2.6.0: rename the selected column
                return true;
            case Key.Delete:
                ClearSelectedCells(Localizer.T("undo.clear_cells"));
                return true;
            case Key.Back:
                if (focused is TextBox b2 && IsDataCell(b2)) { CollapseTo(row, col); EnterEditMode(b2, ""); return true; }
                return false;
            case Key.Escape when SelectedCellCount() > 1:
                CollapseTo(row, col);
                return true;
            case Key.A when ctrl:
                SelectAll();
                return true;
            case Key.X when ctrl:
                _ = CutAsync();
                return true;
            case Key.V when ctrl:
                _ = PasteIntoSelectionAsync();
                return true;
        }
        return false;
    }

    private int FirstVisibleColumn() => NearestVisibleGroupColumn(0);

    private int NextVisibleColumn(int column, int step)
    {
        var n = _session.Doc.Columns.Count;
        for (var c = column + step; c >= 0 && c < n; c += step)
            if (!ColumnGroupOps.IsHidden(_session.Doc, c)) return c;
        return column;
    }

    private int NextVisibleRow(int row, int step)
    {
        var vi = _visibleRows.IndexOf(row);
        if (vi < 0) return _visibleRows.Count > 0 ? _visibleRows[0] : row;
        return _visibleRows[Math.Clamp(vi + step, 0, _visibleRows.Count - 1)];
    }

    /// <summary>Arrow / Tab: the active cell moves by one (the block collapses).</summary>
    private void MoveBy(int row, int column, int dr, int dc)
    {
        if (_session.Doc.Rows.Count == 0) return;
        CommitFocusedEdit();
        var r = dr == 0 ? row : NextVisibleRow(row, dr);
        var c = dc == 0 ? column : NextVisibleColumn(column, dc);
        FocusCell(r, c);
        RefreshSelectionVisuals();
    }

    /// <summary>Shift+arrow: the far corner moves; the active cell keeps the focus.</summary>
    private void ExtendBy(int dr, int dc)
    {
        var r = dr == 0 ? _session.ActiveRow : NextVisibleRow(_session.ActiveRow, dr);
        var c = dc == 0 ? _session.ActiveCol : NextVisibleColumn(_session.ActiveCol, dc);
        _session.ExtendTo(r, c);
        var vi = _visibleRows.IndexOf(r);
        if (vi >= 0) RowsList.ScrollIntoView(vi);
        RefreshSelectionVisuals();
    }

    /// <summary>Ctrl+A / the top-left corner: every cell on screen; the first cell is the active one.</summary>
    private void SelectAll()
    {
        if (_view != ViewMode.Edit || _visibleRows.Count == 0 || _session.Doc.Columns.Count == 0) return;
        CommitFocusedEdit();
        var firstRow = _visibleRows[0];
        var firstCol = FirstVisibleColumn();
        var lastCol = _session.Doc.Columns.Count - 1;
        while (lastCol > 0 && ColumnGroupOps.IsHidden(_session.Doc, lastCol)) lastCol--;
        _session.ChooseCell(firstRow, firstCol);
        _session.ExtendTo(_visibleRows[^1], lastCol);
        FocusCell(firstRow, firstCol, keepRange: true);
        RefreshSelectionVisuals();
        StatusText.Text = Localizer.T("cell.selected_all", SelectedCellCount());
    }

    /// <summary>Click on an empty spot: no selection, no focused cell (an edit in progress is kept first).</summary>
    private void ClearCellSelection()
    {
        CommitFocusedEdit();
        _session.ClearSelection();
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null); // no cell keeps the keyboard
        RefreshSelectionVisuals();
        UpdateStatus();
    }

    // ───────────────────────── clear / copy / cut / paste ─────────────────────────

    /// <summary>Delete: empties every on-screen cell of the block, one undo step. Returns the number of cells emptied.</summary>
    private int ClearSelectedCells(string label)
    {
        var (rows, cols) = SelectedCells();
        if (rows.Count == 0 || cols.Count == 0) return 0;
        CommitFocusedEdit();
        var doc = _session.Doc;
        var before = Snapshot.CloneLite(doc);
        var due = doc.DueColumnIndex();
        var n = 0;
        foreach (var r in rows)
            foreach (var c in cols)
            {
                if (doc.GetCell(r, c).Length == 0) continue;
                doc.SetCellRaw(r, c, "");
                if (c == due) ReminderPlan.Reset(doc, doc.RowIdAt(r), DateTime.Now);
                n++;
            }
        if (n == 0) return 0;
        _undo.Push(new SnapshotAction(label, before, Snapshot.CloneLite(doc)));
        RefreshRowsKeepingSelection(rows);
        ScheduleSave();
        _reminders.CheckNow();
        StatusText.Text = Localizer.T("cell.cleared", n);
        return n;
    }

    /// <summary>Rows on screen are re-created; the focus goes back to the active cell and the block stays.</summary>
    private void RefreshRowsKeepingSelection(IEnumerable<int> rows)
    {
        var hadFocus = FocusInRows(); // a selected column header keeps the focus (no jump to the top row)
        _ignoreText = true;
        try
        {
            foreach (var c in RowsList.GetRealizedContainers())
                if (c is ContentPresenter { Child: Grid { Tag: int r } g } && rows.Contains(r)) PopulateRow(g, r);
        }
        finally { _ignoreText = false; }
        if (hadFocus && _session.AnchorRow >= 0) FocusCell(_session.AnchorRow, _session.AnchorCol, keepRange: true);
        if (FindOpen) RefreshFindMatches();
        RefreshSelectionVisuals();
    }

    /// <summary>Tab-separated text of the on-screen cells of the block, status as its label (pastes into Excel as shown).</summary>
    private string? SelectionText()
    {
        var (rows, cols) = SelectedCells();
        if (rows.Count == 0 || cols.Count == 0) return null;
        var doc = _session.Doc;
        var status = doc.StatusColumnIndex();
        var lines = rows.Select(r => (IReadOnlyList<string>)cols.Select(c =>
        {
            var v = doc.GetCell(r, c);
            return c == status && v.Length > 0 ? TaskStatuses.Label(TaskStatuses.DisplayCode(v)) : v;
        }).ToList()).ToList();
        return ExcelCopy.Write(lines, includeHeaders: false, null);
    }

    private async Task CutAsync()
    {
        if (!await CopySelectionAsync()) return;
        ClearSelectedCells(Localizer.T("undo.cut_cells"));
        StatusText.Text = Localizer.T("cell.cut", SelectedCellCount());
    }

    private async Task<bool> CopySelectionAsync()
    {
        var text = SelectionText();
        if (text == null) { StatusText.Text = Localizer.T("ui.copy_too_big"); return false; }
        if (Clipboard == null) { StatusText.Text = Localizer.T("ui.copy_failed"); return false; }
        await Clipboard.SetTextAsync(text);
        RememberCopy(text); // 2.6.0: Insert copied brings rows / columns back with their data
        var n = SelectedCellCount();
        StatusText.Text = n > 1 ? Localizer.T("cell.copied", n) : Localizer.T("ui.copied_selection");
        return true;
    }

    /// <summary>Ctrl+V on a selected cell: a block from Excel goes in from the block's top-left cell; a single value
    /// fills every selected cell (like Excel).</summary>
    private async Task PasteIntoSelectionAsync()
    {
        if (SelectionRect() is not { } s || Clipboard == null) return;
        string? text;
        try { text = await Clipboard.TryGetTextAsync(); }
        catch (Exception ex) { StatusText.Text = Localizer.T("ui.paste_failed_reason", ex.Message); return; }
        if (string.IsNullOrEmpty(text) || _view != ViewMode.Edit) return;
        if (text.Length > TableLimits.MaxPasteChars) { StatusText.Text = TableLimits.PasteTooBigMessage; return; }
        var trimmed = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        var (rows, cols) = SelectedCells();
        if (rows.Count == 0 || cols.Count == 0) return;
        if (ExcelPaste.LooksLikeGrid(trimmed))
        {
            CommitFocusedEdit();
            _ignoreText = true;
            try { ApplyPastedGrid(null, header: false, rows[0], cols[0], text); }
            finally { _ignoreText = false; }
            return;
        }
        var value = TableLimits.Clip(trimmed, TableLimits.MaxCellChars);
        CommitFocusedEdit();
        var doc = _session.Doc;
        var before = Snapshot.CloneLite(doc);
        var due = doc.DueColumnIndex();
        var n = 0;
        foreach (var r in rows)
            foreach (var c in cols)
            {
                if (doc.GetCell(r, c) == value) continue;
                doc.SetCellRaw(r, c, value);
                if (c == due) ReminderPlan.Reset(doc, doc.RowIdAt(r), DateTime.Now);
                n++;
            }
        if (n == 0) return;
        _undo.Push(new SnapshotAction(Localizer.T("undo.paste_excel"), before, Snapshot.CloneLite(doc)));
        RefreshRowsKeepingSelection(rows);
        ScheduleSave();
        _reminders.CheckNow();
        StatusText.Text = Localizer.T("cell.pasted", n);
    }

    // ───────────────────────── cell menu ─────────────────────────

    /// <param name="box">The cell's text box (null for status, date, check and days cells: no Edit item).</param>
    private void ShowCellMenu(Control anchor, TextBox? box, int row, int column)
    {
        if (_view != ViewMode.Edit) return;
        var editing = box is { IsReadOnly: false };
        MenuItem Item(string key, string gesture, Action act, bool enabled = true)
        {
            var m = new MenuItem { Header = Localizer.T(key), InputGesture = KeyGesture.Parse(gesture), IsEnabled = enabled };
            m.Click += (_, _) => act();
            return m;
        }
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("cell.menu_copy", "Ctrl+C", () => { if (editing) box!.Copy(); else _ = CopySelectionAsync(); }));
        flyout.Items.Add(Item("cell.menu_cut", "Ctrl+X", () => { if (editing) box!.Cut(); else _ = CutAsync(); }));
        flyout.Items.Add(Item("cell.menu_paste", "Ctrl+V", () => { if (editing) box!.Paste(); else _ = PasteIntoSelectionAsync(); }));
        if (!editing)
            foreach (var item in CellStructureMenuItems(row, column)) flyout.Items.Add(item); // 2.6.0: insert copied, insert ▸, delete ▸
        flyout.Items.Add(Item("cell.menu_clear", "Delete", () => ClearSelectedCells(Localizer.T("undo.clear_cells")), !editing));
        flyout.Items.Add(new Separator());
        if (box != null)
            flyout.Items.Add(Item("cell.menu_edit", "F2", () => { CollapseTo(row, column); EnterEditMode(box, null); }, !editing));
        flyout.Items.Add(Item("cell.menu_select_all", "Ctrl+A", SelectAll));
        _lastCellMenu = flyout; // tests read the menu that was shown
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    /// <summary>Column letter of a column position: A … Z, AA … (as in Excel).</summary>
    public static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        for (var n = index + 1; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)('A' + (n - 1) % 26));
        return sb.ToString();
    }
}
