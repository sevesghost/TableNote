using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>v2.4.0: move / delete columns — drag grip on the header, header right-click menu,
/// Alt+Shift+←/→. Every change is one undo step (SnapshotStep) and is saved at once (FlushSave).</summary>
public partial class MainWindow
{
    private static readonly Cursor ColumnMoveCursor = new(StandardCursorType.SizeAll);
    private static readonly IBrush ColumnDropMark = new SolidColorBrush(Color.Parse("#C0392B"));

    /// <summary>Extra items other features (e.g. "Column settings…") can add to the same header menu.</summary>
    private Func<int, IEnumerable<Control>>? _extraColumnMenuItems;

    private void RequestMoveColumn(int from, int to) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.move_column"), () => MoveColumnAsync(from, to)));

    private void RequestDeleteColumn(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("delete.undo_column"), () => DeleteColumnAtAsync(column)));

    private Task MoveColumnAsync(int from, int to)
    {
        var r = _session.MoveColumn(from, to);
        if (!r.Changed)
        {
            if (!string.IsNullOrEmpty(r.Status)) StatusText.Text = r.Status;
            return Task.CompletedTask;
        }
        Rebuild(); FlushSave();
        var dest = Math.Clamp(to, 0, _session.Doc.Columns.Count - 1);
        StatusText.Text = Localizer.T("col.moved", _session.Doc.Columns.ElementAtOrDefault(dest) ?? "", dest + 1);
        AppLog.Write("table", $"column moved {from + 1} -> {dest + 1}");
        return Task.CompletedTask;
    }

    private async Task DeleteColumnAtAsync(int column)
    {
        _busy = true;
        try
        {
            var r = await _session.DeleteColumnAtAsync(column, _confirm.ConfirmAsync);
            if (!r.Changed)
            {
                if (!string.IsNullOrEmpty(r.Status)) StatusText.Text = r.Status;
                if (r.Status == DeletePlanner.DueColumnProtected) ShowNotice(r.Status);
            }
            else
            {
                Rebuild(); FlushSave();
                if (_session.Doc.DueColumnIndex() < 0)
                    ShowNotice(Localizer.T("ui.no_due_column"));
            }
        }
        finally { _busy = false; }
    }

    /// <summary>Alt+Shift+←/→ moves the active column. Returns true when the key was handled.</summary>
    private bool HandleColumnMoveKeys(KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Alt | KeyModifiers.Shift) || e.Key is not (Key.Left or Key.Right)) return false;
        if (_view != ViewMode.Edit) return false;
        e.Handled = true;
        if (_busy) return true;
        var from = _session.TargetColumn();
        RequestMoveColumn(from, from + (e.Key == Key.Left ? -1 : 1));
        return true;
    }

    /// <summary>Wraps the existing header (name box + resize thumb) with a drag grip on the left and the column menu.</summary>
    private Control WithColumnGrip(int column, TextBox box, Control header)
    {
        var grip = new Border
        {
            Width = 14, Background = HeaderBg, BorderBrush = GridLine, BorderThickness = new Thickness(1, 1, 0, 1),
            Cursor = ColumnMoveCursor, IsHitTestVisible = _view == ViewMode.Edit,
            Child = new TextBlock
            {
                Text = "⋮⋮", FontSize = 11, Foreground = TextFg, Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTip.SetTip(grip, Localizer.T("col.grip_tip"));
        Point? start = null;
        var dragging = false;
        var dropAt = column;
        void Reset() { start = null; dragging = false; ShowColumnDropTarget(column, -1); }
        grip.PointerPressed += (_, e) =>
        {
            if (_view != ViewMode.Edit || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(TableGrid);
            dragging = false;
            dropAt = column;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (start is not { } st) return;
            var p = e.GetPosition(TableGrid);
            if (!dragging && Math.Abs(p.X - st.X) < 6) return;
            dragging = true;
            dropAt = ColumnAtX(p.X);
            ShowColumnDropTarget(column, dropAt);
            StatusText.Text = dropAt == column ? Localizer.T("col.drag_hint") : Localizer.T("col.drop_at", dropAt + 1);
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (start == null) return;
            var go = dragging && dropAt != column;
            var wasDragging = dragging;
            Reset();
            e.Pointer.Capture(null);
            if (go) RequestMoveColumn(column, dropAt);
            else if (wasDragging) StatusText.Text = "";
            e.Handled = true;
        };
        grip.PointerCaptureLost += (_, _) => Reset();

        box.ContextFlyout = null;
        box.ContextMenu = BuildColumnMenu(column, box);
        grip.ContextMenu = BuildColumnMenu(column, null);

        var wrap = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        Grid.SetColumn(header, 1);
        wrap.Children.Add(grip);
        wrap.Children.Add(header);
        return wrap;
    }

    /// <summary>Header menu: cut/copy/paste of the header text, move, delete (always confirmed), plus other teams' items.</summary>
    private ContextMenu BuildColumnMenu(int column, TextBox? box)
    {
        var count = _session.Doc.Columns.Count;
        var edit = _view == ViewMode.Edit;
        var items = new List<Control>();
        if (box != null)
        {
            var cut = new MenuItem { Header = Localizer.T("col.cut"), InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
            cut.Click += (_, _) => box.Cut();
            var copy = new MenuItem { Header = Localizer.T("col.copy"), InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
            copy.Click += (_, _) => box.Copy();
            var paste = new MenuItem { Header = Localizer.T("col.paste"), InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
            paste.Click += (_, _) => box.Paste();
            items.AddRange([cut, copy, paste, new Separator()]);
        }
        MenuItem Item(string text, bool enabled, Action act, KeyGesture? g = null)
        {
            var m = new MenuItem { Header = text, IsEnabled = enabled && edit };
            if (g != null) m.InputGesture = g;
            m.Click += (_, _) => act();
            return m;
        }
        items.Add(Item(Localizer.T("col.move_left"), column > 0, () => RequestMoveColumn(column, column - 1),
            new KeyGesture(Key.Left, KeyModifiers.Alt | KeyModifiers.Shift)));
        items.Add(Item(Localizer.T("col.move_right"), column < count - 1, () => RequestMoveColumn(column, column + 1),
            new KeyGesture(Key.Right, KeyModifiers.Alt | KeyModifiers.Shift)));
        items.Add(Item(Localizer.T("col.move_first"), column > 0, () => RequestMoveColumn(column, 0)));
        items.Add(Item(Localizer.T("col.move_last"), column < count - 1, () => RequestMoveColumn(column, count - 1)));
        if (_extraColumnMenuItems?.Invoke(column) is { } extra)
        {
            items.Add(new Separator());
            items.AddRange(extra);
        }
        items.Add(new Separator());
        var isDue = column == _session.Doc.DueColumnIndex();
        items.Add(Item(isDue ? Localizer.T("col.delete_due") : Localizer.T("col.delete"), !isDue && count > 1,
            () => RequestDeleteColumn(column)));
        return new ContextMenu { ItemsSource = items };
    }

    /// <summary>Data column under an X position in TableGrid (grid column 0 = row numbers).</summary>
    private int ColumnAtX(double x)
    {
        var acc = RowLabelWidth;
        var n = _session.Doc.Columns.Count;
        for (var i = 0; i < n; i++)
        {
            var w = i + 1 < TableGrid.ColumnDefinitions.Count ? TableGrid.ColumnDefinitions[i + 1].ActualWidth : 0;
            if (w <= 0) w = _session.Doc.ColumnWidth(i);
            if (x < acc + w) return i;
            acc += w;
        }
        return Math.Max(0, n - 1);
    }

    /// <summary>Marks the header the dragged column will land on (-1 clears).</summary>
    private void ShowColumnDropTarget(int from, int target)
    {
        for (var i = 0; i < _headerBoxes.Count; i++)
        {
            if (_headerBoxes[i] is not TextBox tb) continue;
            var on = target >= 0 && i == target && target != from;
            tb.BorderBrush = on ? ColumnDropMark : GridLine;
            tb.BorderThickness = on ? (target < from ? new Thickness(4, 1, 1, 1) : new Thickness(1, 1, 4, 1)) : new Thickness(1);
        }
    }
}
