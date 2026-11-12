using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>
/// v2.4.0: move rows — drag the row number, right-click menu on the row number, Alt+Shift+↑/↓.
/// Logic lives in Core/RowOps.cs; undo uses RowMoveAction (never SnapshotAction, see RowMoveAction).
/// With "Hide done" on, moves are between VISIBLE rows (hidden rows keep their place relative to each other).
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush RowDropLineBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
    private static readonly Cursor RowDragCursor = new(StandardCursorType.SizeNorthSouth);
    private const double RowDragThreshold = 4;

    private int _rowDragRow = -1;
    private Point _rowDragStart;
    private bool _rowDragging;
    private int _rowDropSlot = -1;
    private Border? _rowDropLine;

    /// <summary>
    /// Extension point for other features (e.g. the row style/highlight items): given the row id, return extra
    /// menu entries appended (after a separator) to the row-number context menu. Called each time the menu opens.
    /// </summary>
    private Func<string, IEnumerable<Control>>? _extraRowMenuItems = null; // set by the feature that plugs in

    /// <summary>Hook called from PopulateRow for the row-number cell.</summary>
    private void AttachRowMove(Border label, int rowIndex)
    {
        if (_view != ViewMode.Edit) return;
        label.Cursor = RowDragCursor;
        ToolTip.SetTip(label, Localizer.T("row.drag_tip"));
        label.ContextMenu = BuildRowMenu(rowIndex);
        // handledEventsToo: the existing PointerPressed handler (select row) marks the event handled.
        label.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(label).Properties.IsLeftButtonPressed || _busy) return;
            _rowDragRow = rowIndex;
            _rowDragging = false;
            _rowDragStart = e.GetPosition(RowsList);
            e.Pointer.Capture(label);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        label.PointerMoved += (_, e) =>
        {
            if (_rowDragRow != rowIndex) return;
            var p = e.GetPosition(RowsList);
            if (!_rowDragging)
            {
                if (Math.Abs(p.Y - _rowDragStart.Y) < RowDragThreshold) return;
                _rowDragging = true;
                ToolTip.SetIsOpen(label, false);
            }
            AutoScrollRows(p.Y);
            ShowRowDropLine(RowDropSlotAt(e.GetPosition(RowsList).Y));
            e.Handled = true;
        };
        label.PointerReleased += (_, e) =>
        {
            if (_rowDragRow != rowIndex) return;
            var dragged = _rowDragging;
            var slot = _rowDropSlot;
            EndRowDrag();
            e.Pointer.Capture(null);
            if (!dragged) return;
            e.Handled = true;
            var target = RowOps.TargetForSlot(_visibleRows, rowIndex, slot);
            if (target >= 0) _ = RunStructureAsync(() => { MoveRowTo(rowIndex, target, focus: false); return Task.CompletedTask; });
        };
        label.PointerCaptureLost += (_, _) => { if (_rowDragRow == rowIndex) EndRowDrag(); };
    }

    private ContextMenu BuildRowMenu(int rowIndex)
    {
        var menu = new ContextMenu { FontFamily = AppFonts.Thai };
        var items = new (RowMoveKind kind, string key, KeyGesture? gesture)[]
        {
            (RowMoveKind.Up, "row.move_up", new KeyGesture(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift)),
            (RowMoveKind.Down, "row.move_down", new KeyGesture(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift)),
            (RowMoveKind.Top, "row.move_top", null),
            (RowMoveKind.Bottom, "row.move_bottom", null),
        };
        var built = new List<(MenuItem item, RowMoveKind kind)>();
        foreach (var (kind, key, gesture) in items)
        {
            var mi = new MenuItem { Header = Localizer.T(key), InputGesture = gesture, FontFamily = AppFonts.Thai };
            mi.Click += (_, _) => _ = RunStructureAsync(() => { MoveRowBy(rowIndex, kind, focus: false); return Task.CompletedTask; });
            menu.Items.Add(mi);
            built.Add((mi, kind));
        }
        var fixedCount = menu.Items.Count;
        menu.Opening += (_, _) =>
        {
            _session.ChooseRow(rowIndex);
            Highlight();
            UpdateStatus();
            foreach (var (mi, kind) in built)
                mi.IsEnabled = !_busy && RowOps.TargetFor(_visibleRows, rowIndex, kind) >= 0;
            // Re-create the extension entries on every open so they reflect the row's current state.
            while (menu.Items.Count > fixedCount) menu.Items.RemoveAt(menu.Items.Count - 1);
            if (_extraRowMenuItems == null || rowIndex < 0 || rowIndex >= _session.Doc.Rows.Count) return;
            var extra = _extraRowMenuItems(_session.Doc.RowIdAt(rowIndex))?.ToList();
            if (extra is not { Count: > 0 }) return;
            menu.Items.Add(new Separator());
            foreach (var c in extra) menu.Items.Add(c);
        };
        return menu;
    }

    /// <summary>Alt+Shift+↑/↓ moves the selected row (columns use Alt+Shift+←/→).</summary>
    private bool HandleRowMoveKey(KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != (KeyModifiers.Alt | KeyModifiers.Shift)) return false;
        e.Handled = true;
        if (_busy || !_session.HasSelection) return true;
        var row = _session.ActiveRow;
        var kind = e.Key == Key.Up ? RowMoveKind.Up : RowMoveKind.Down;
        _ = RunStructureAsync(() => { MoveRowBy(row, kind, focus: true); return Task.CompletedTask; });
        return true;
    }

    private void MoveRowBy(int row, RowMoveKind kind, bool focus)
    {
        var target = RowOps.TargetFor(_visibleRows, row, kind);
        if (target < 0) { StatusText.Text = Localizer.T("row.cannot_move"); return; }
        MoveRowTo(row, target, focus);
    }

    /// <summary>One move = one undo step (RowMoveAction); the selection follows its row; saved immediately.</summary>
    private void MoveRowTo(int row, int target, bool focus)
    {
        var doc = _session.Doc;
        doc.EnsureRowIds();
        if (row < 0 || row >= doc.Rows.Count) return;
        var id = doc.RowIdAt(row);
        var activeId = _session.HasSelection && _session.ActiveRow >= 0 && _session.ActiveRow < doc.Rows.Count
            ? doc.RowIdAt(_session.ActiveRow) : null;
        if (!RowOps.MoveRow(doc, row, target)) return;
        _undo.Push(new RowMoveAction(id, row, target, Localizer.T("row.undo_label")));
        var col = Math.Max(0, _session.ActiveCol);
        var follow = activeId == null ? -1 : FindRowById(activeId);
        if (follow >= 0) _session.ChooseCell(follow, col);
        Rebuild();
        if (focus && follow >= 0) FocusCell(follow, col);
        FlushSave();
        UpdateStatus();
        StatusText.Text = Localizer.T("row.moved", row + 1, target + 1);
    }

    /// <summary>Gap index in the visible list under y (relative to RowsList): 0 = above the first visible row.</summary>
    private int RowDropSlotAt(double y)
    {
        int? firstVi = null, lastVi = null;
        foreach (var c in RowsList.GetRealizedContainers())
        {
            if (c is not ContentPresenter { Child: Grid { Tag: int r } g }) continue;
            var vi = _visibleRows.IndexOf(r);
            var top = g.TranslatePoint(new Point(0, 0), RowsList);
            if (vi < 0 || top is null) continue;
            if (firstVi is null || vi < firstVi) firstVi = vi;
            if (lastVi is null || vi > lastVi) lastVi = vi;
            var mid = top.Value.Y + g.Bounds.Height / 2;
            if (y >= top.Value.Y && y < top.Value.Y + g.Bounds.Height) return y < mid ? vi : vi + 1;
        }
        if (firstVi is null) return -1;
        return y < 0 ? firstVi.Value : lastVi!.Value + 1;
    }

    private void ShowRowDropLine(int slot)
    {
        if (slot == _rowDropSlot && _rowDropLine != null) return;
        RemoveRowDropLine();
        _rowDropSlot = slot;
        if (slot < 0 || _visibleRows.Count == 0) return;
        var atBottom = slot >= _visibleRows.Count;
        var rowIndex = _visibleRows[atBottom ? _visibleRows.Count - 1 : slot];
        foreach (var c in RowsList.GetRealizedContainers())
        {
            if (c is not ContentPresenter { Child: Grid { Tag: int r } g } || r != rowIndex) continue;
            _rowDropLine = new Border
            {
                Height = 3, Background = RowDropLineBrush, IsHitTestVisible = false,
                VerticalAlignment = atBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                Margin = new Thickness(0, atBottom ? 0 : -1, 0, atBottom ? -1 : 0)
            };
            Grid.SetColumnSpan(_rowDropLine, Math.Max(1, g.ColumnDefinitions.Count));
            g.Children.Add(_rowDropLine);
            return;
        }
    }

    private void RemoveRowDropLine()
    {
        if (_rowDropLine?.Parent is Panel p) p.Children.Remove(_rowDropLine);
        _rowDropLine = null;
    }

    private void EndRowDrag()
    {
        _rowDragRow = -1;
        _rowDragging = false;
        _rowDropSlot = -1;
        RemoveRowDropLine();
    }

    private void AutoScrollRows(double y)
    {
        if (RowsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } sv) return;
        const double edge = 24, step = 16;
        var h = RowsList.Bounds.Height;
        if (y < edge) sv.Offset = sv.Offset.WithY(Math.Max(0, sv.Offset.Y - step));
        else if (y > h - edge) sv.Offset = sv.Offset.WithY(sv.Offset.Y + step);
    }
}
