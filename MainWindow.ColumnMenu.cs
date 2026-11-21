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
                AfterColumnRemoved();
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

    /// <summary>Header menu: cut/copy/paste (the column's cells; the name while it is being renamed), insert, move, delete
    /// (always confirmed), plus other teams' items. Built when it opens, so the counts match the selected columns.</summary>
    private ContextMenu BuildColumnMenu(int column, TextBox? box)
    {
        var menu = new ContextMenu { FontFamily = AppFonts.Thai };
        void Fill()
        {
            menu.Items.Clear();
            foreach (var item in BuildColumnMenuItems(column, box)) menu.Items.Add(item);
        }
        menu.Opening += (_, _) =>
        {
            if (box is not { IsReadOnly: false } && !(WholeColumnsSelected() && ColumnSelected(column))) SelectColumns(column, column);
            Fill();
        };
        Fill();
        return menu;
    }

    /// <summary>
    /// Items for the header menu and for the top Column menu. <paramref name="keepClipboardSlot"/>
    /// keeps Cut / Copy / Paste visible (disabled) when there is no header box, which the menu bar
    /// needs for a collapsed column. The grip context menu leaves the flag off and omits them.
    /// </summary>
    internal List<Control> BuildColumnMenuItems(int column, TextBox? box, bool keepClipboardSlot = false)
    {
        var count = _session.Doc.Columns.Count;
        var edit = _view == ViewMode.Edit;
        var items = new List<Control>();
        if (box != null || keepClipboardSlot)
        {
            var clip = MenuCommandRules.ColumnClipboard(edit, box != null, column, count);
            var cut = new MenuItem { Header = Localizer.T("col.cut"), InputGesture = new KeyGesture(Key.X, KeyModifiers.Control), IsEnabled = clip };
            var copy = new MenuItem { Header = Localizer.T("col.copy"), InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsEnabled = clip };
            var paste = new MenuItem { Header = Localizer.T("col.paste"), InputGesture = new KeyGesture(Key.V, KeyModifiers.Control), IsEnabled = clip };
            if (box != null)
            {
                // 2.6.0: the column's cells, like Excel; while the name is being edited, the name's text.
                cut.Click += (_, _) => { if (!box.IsReadOnly) box.Cut(); else { EnsureColumnsSelected(column); _ = CutAsync(); } };
                copy.Click += (_, _) => { if (!box.IsReadOnly) box.Copy(); else { EnsureColumnsSelected(column); _ = CopySelectionAsync(); } };
                paste.Click += (_, _) => { if (!box.IsReadOnly) box.Paste(); else { EnsureColumnsSelected(column); _ = PasteIntoSelectionAsync(); } };
            }
            items.AddRange([cut, copy, paste]);
            items.AddRange(ColumnEditMenuItems(column)); // 2.6.0: insert copied, insert left / right, clear
            items.Add(new Separator());
        }
        else
        {
            items.AddRange(ColumnEditMenuItems(column));
            items.Add(new Separator());
        }
        MenuItem Item(string text, bool enabled, Action act, KeyGesture? g = null)
        {
            var m = new MenuItem { Header = text, IsEnabled = enabled };
            if (g != null) m.InputGesture = g;
            m.Click += (_, _) => act();
            return m;
        }
        items.Add(Item(Localizer.T("col.move_left"), MenuCommandRules.MoveLeft(edit, column, count), () => RequestMoveColumn(column, column - 1),
            new KeyGesture(Key.Left, KeyModifiers.Alt | KeyModifiers.Shift)));
        items.Add(Item(Localizer.T("col.move_right"), MenuCommandRules.MoveRight(edit, column, count), () => RequestMoveColumn(column, column + 1),
            new KeyGesture(Key.Right, KeyModifiers.Alt | KeyModifiers.Shift)));
        items.Add(Item(Localizer.T("col.move_first"), MenuCommandRules.MoveFirst(edit, column, count), () => RequestMoveColumn(column, 0)));
        items.Add(Item(Localizer.T("col.move_last"), MenuCommandRules.MoveLast(edit, column, count), () => RequestMoveColumn(column, count - 1)));
        if (_extraColumnMenuItems?.Invoke(column) is { } extra)
        {
            items.Add(new Separator());
            items.AddRange(extra);
        }
        items.Add(new Separator());
        items.AddRange(BuildColumnGroupMenuItems(column, edit));
        items.Add(new Separator());
        // Status and the reminder column use the same delete item as every other column.
        var selected = ColumnsFor(column).Count; // 2.6.0: every selected column
        items.Add(Item(selected > 1 ? Localizer.T("cols.delete", selected) : Localizer.T("col.delete"),
            MenuCommandRules.DeleteColumn(edit, column, count, isDue: false) && selected < count,
            () => { if (selected > 1) _ = RunStructureAsync(() => DeleteColumnsAsync(column)); else RequestDeleteColumn(column); }));
        return items;
    }

    /// <summary>Data column under an X position in TableGrid (grid column 0 = row numbers).</summary>
    private int ColumnAtX(double x)
    {
        var acc = RowLabelWidth;
        var n = _session.Doc.Columns.Count;
        for (var i = 0; i < n; i++)
        {
            var w = i + 1 < TableGrid.ColumnDefinitions.Count ? TableGrid.ColumnDefinitions[i + 1].ActualWidth : 0;
            // A collapsed child is width 0 on purpose — do not fall back to its stored width or drag-drop hits it.
            if (w <= 0) w = ColumnGroupOps.IsHidden(_session.Doc, i) ? 0 : _session.Doc.ColumnWidth(i);
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

    /// <summary>▸ / ▾ on a group parent. Collapsing hides children; it does not change their cells.</summary>
    private Control MakeGroupToggle(int column)
    {
        var doc = _session.Doc;
        var group = ColumnGroupOps.GroupForParent(doc, column);
        var expanded = group?.Expanded ?? true;
        var parentId = doc.ColumnIdAt(column);
        var button = new Button
        {
            Content = expanded ? "▾" : "▸",
            Width = 28, MinWidth = 28, Padding = new Thickness(0),
            Background = HeaderBg, Foreground = TextFg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 0),
            FontSize = 16, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = _view == ViewMode.Edit,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var count = group?.ChildIds.Count ?? 0;
        ToolTip.SetTip(button, expanded
            ? Localizer.T("colgroup.toggle_collapse_tip")
            : Localizer.T("colgroup.toggle_expand_tip", count));
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (_busy || _view != ViewMode.Edit) return;
            var idx = _session.Doc.ColumnIds.IndexOf(parentId);
            if (idx >= 0) RequestToggleGroup(idx);
        };
        return button;
    }

    private List<Control> BuildColumnGroupMenuItems(int column, bool edit)
    {
        var doc = _session.Doc;
        var items = new List<Control>();
        var parent = ColumnGroupOps.GroupForParent(doc, column);
        var child = parent == null ? ColumnGroupOps.GroupForChild(doc, column) : null;
        if (parent != null)
        {
            var parentId = doc.ColumnIdAt(column);
            items.Add(GroupItem(parent.Expanded ? Localizer.T("colgroup.collapse") : Localizer.T("colgroup.expand"), MenuCommandRules.ToggleGroup(edit), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(parentId);
                if (idx >= 0) RequestToggleGroup(idx);
            }));
            var room = NoteDocument.MaxUserColumns - doc.Columns.Count;
            items.Add(GroupItem(Localizer.T("colgroup.add_child"), MenuCommandRules.AddChild(edit, room, parent.ChildIds.Count), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(parentId);
                if (idx >= 0) RequestAddChild(idx);
            }));
            items.Add(GroupItem(Localizer.T("colgroup.rename_children"), MenuCommandRules.RenameChildren(edit), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(parentId);
                if (idx >= 0) RequestRenameChildren(idx);
            }));
            var remove = new MenuItem { Header = Localizer.T("colgroup.remove_child"), IsEnabled = MenuCommandRules.RemoveChildParent(edit) };
            foreach (var cid in parent.ChildIds.ToList())
            {
                var childIndex = doc.ColumnIds.IndexOf(cid);
                var label = childIndex >= 0 ? doc.Columns[childIndex] : cid;
                var idCopy = cid;
                var m = new MenuItem { Header = Localizer.T("colgroup.remove_named", label), IsEnabled = MenuCommandRules.RemoveNamedChild(edit) };
                m.Click += (_, _) =>
                {
                    var idx = _session.Doc.ColumnIds.IndexOf(idCopy);
                    if (idx >= 0) RequestDeleteColumn(idx);
                };
                remove.Items.Add(m);
            }
            items.Add(remove);
            items.Add(GroupItem(Localizer.T("colgroup.ungroup"), MenuCommandRules.Ungroup(edit), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(parentId);
                if (idx >= 0) RequestUngroup(idx);
            }));
        }
        else if (child != null)
        {
            var parentIndex = doc.ColumnIds.IndexOf(child.ParentId);
            var parentName = parentIndex >= 0 ? doc.Columns[parentIndex] : "";
            items.Add(new MenuItem { Header = Localizer.T("colgroup.child_of", parentName), IsEnabled = MenuCommandRules.ChildOfLabel() });
            var childId = doc.ColumnIdAt(column);
            items.Add(GroupItem(Localizer.T("colgroup.remove_named", doc.Columns[column]), MenuCommandRules.RemoveThisColumn(edit, doc.Columns.Count), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(childId);
                if (idx >= 0) RequestDeleteColumn(idx);
            }));
        }
        else
        {
            var id = column >= 0 && column < doc.Columns.Count ? doc.ColumnIdAt(column) : "";
            var room = NoteDocument.MaxUserColumns - doc.Columns.Count;
            var system = column >= 0 && ColumnSettingsOps.IsSystemColumn(doc, column);
            var make = GroupItem(Localizer.T("colgroup.make"), MenuCommandRules.MakeGroup(edit, column, doc.Columns.Count, system, room), () =>
            {
                var idx = _session.Doc.ColumnIds.IndexOf(id);
                if (idx >= 0) RequestMakeGroup(idx);
            });
            ToolTip.SetTip(make, system ? Localizer.T("colgroup.system") : Localizer.T("colgroup.make_tip"));
            items.Add(make);
        }
        return items;
    }

    private MenuItem GroupItem(string text, bool enabled, Action act)
    {
        var m = new MenuItem { Header = text, IsEnabled = enabled };
        m.Click += (_, _) => act();
        return m;
    }

    private void RequestMakeGroup(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.colgroup_create"), () => MakeGroupAsync(column)));

    private void RequestAddChild(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.colgroup_add"), () => Task.FromResult(ApplyGroupEdit(column, ColumnGroupOps.AddChild, "colgroup.added"))));

    private void RequestUngroup(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.colgroup_ungroup"), () => Task.FromResult(ApplyGroupEdit(column, ColumnGroupOps.Ungroup, "colgroup.ungrouped"))));

    // View state only: persisted, but not an undo step, so Ctrl+Z skips it.
    private void RequestToggleGroup(int column) =>
        _ = RunStructureAsync(() => ToggleGroupAsync(column));

    private void RequestRenameChildren(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.colgroup_rename"), () => RenameChildrenAsync(column)));

    private bool ApplyGroupEdit(int column, Func<NoteDocument, int, GroupEdit> edit, string doneKey)
    {
        var doc = _session.Doc;
        if (column < 0 || column >= doc.Columns.Count) return false;
        var id = doc.ColumnIdAt(column);
        var result = edit(doc, column);
        if (!result.Ok)
        {
            if (!string.IsNullOrEmpty(result.Error)) StatusText.Text = result.Error;
            return false;
        }
        column = doc.ColumnIds.IndexOf(id);
        Rebuild(); FlushSave();
        var name = column >= 0 ? doc.Columns[column] : "";
        StatusText.Text = Localizer.T(doneKey, name);
        return true;
    }

    private async Task MakeGroupAsync(int column)
    {
        var doc = _session.Doc;
        if (column < 0 || column >= doc.Columns.Count || _view != ViewMode.Edit) return;
        var id = doc.ColumnIdAt(column);
        var room = NoteDocument.MaxUserColumns - doc.Columns.Count;
        if (room < 1)
        {
            StatusText.Text = Localizer.T("colgroup.no_room", ColumnGroupOps.DefaultChildCount, NoteDocument.MaxUserColumns);
            return;
        }
        _busy = true;
        try
        {
            var count = await new ColumnGroupCountDialog(doc.Columns[column], room).ShowDialog<int?>(this);
            if (count is not int n) return;
            column = doc.ColumnIds.IndexOf(id);
            if (column < 0) { StatusText.Text = Localizer.T("colgroup.not_column"); return; }
            var result = ColumnGroupOps.Create(doc, column, n);
            if (!result.Ok) { StatusText.Text = result.Error ?? ""; return; }
            Rebuild(); FlushSave();
            StatusText.Text = Localizer.T("colgroup.created", doc.Columns[column], n);
        }
        finally { _busy = false; }
    }

    private Task ToggleGroupAsync(int column)
    {
        var doc = _session.Doc;
        if (column < 0 || column >= doc.Columns.Count) return Task.CompletedTask;
        var id = doc.ColumnIdAt(column);
        var before = ColumnGroupOps.GroupForParent(doc, column)?.Expanded ?? true;
        var result = ColumnGroupOps.Toggle(doc, column);
        if (!result.Ok) { StatusText.Text = result.Error ?? ""; return Task.CompletedTask; }
        column = doc.ColumnIds.IndexOf(id);
        Rebuild(); FlushSave();
        var name = column >= 0 ? doc.Columns[column] : "";
        StatusText.Text = Localizer.T(before ? "colgroup.collapsed" : "colgroup.expanded", name);
        return Task.CompletedTask;
    }

    private async Task RenameChildrenAsync(int column)
    {
        var doc = _session.Doc;
        if (column < 0 || column >= doc.Columns.Count || _view != ViewMode.Edit) return;
        var id = doc.ColumnIdAt(column);
        var group = ColumnGroupOps.GroupForParent(doc, column);
        if (group == null) { StatusText.Text = Localizer.T("colgroup.not_group"); return; }
        var names = group.ChildIds.Select(cid =>
        {
            var i = doc.ColumnIds.IndexOf(cid);
            return i >= 0 ? doc.Columns[i] : "";
        }).ToList();
        _busy = true;
        try
        {
            var edited = await new ColumnGroupNamesDialog(doc.Columns[column], names).ShowDialog<List<string>?>(this);
            if (edited == null) return;
            column = doc.ColumnIds.IndexOf(id);
            if (column < 0) { StatusText.Text = Localizer.T("colgroup.not_column"); return; }
            var result = ColumnGroupOps.RenameChildren(doc, column, edited);
            if (!result.Ok) { StatusText.Text = result.Error ?? ""; return; }
            Rebuild(); FlushSave();
            StatusText.Text = Localizer.T("colgroup.renamed", doc.Columns[column]);
        }
        finally { _busy = false; }
    }
}

/// <summary>How many child columns a new group should own. Default 3, capped by the free column slots.</summary>
public sealed class ColumnGroupCountDialog : Window
{
    public ColumnGroupCountDialog(string parentName, int room)
    {
        Title = Localizer.T("colgroup.ask_count_title");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        var max = Math.Max(1, Math.Min(ColumnGroupOps.MaxChildCount, room));
        var start = Math.Min(ColumnGroupOps.DefaultChildCount, max);
        var count = new NumericUpDown
        {
            Minimum = ColumnGroupOps.MinChildCount, Maximum = max, Increment = 1,
            Value = start, FormatString = "0", Width = 120, HorizontalAlignment = HorizontalAlignment.Left
        };
        var ok = new Button { Content = Localizer.T("common.ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var n = (int)(count.Value ?? start);
            if (n < ColumnGroupOps.MinChildCount || n > max)
            {
                return;
            }
            Close(n);
        };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = Localizer.T("colgroup.ask_count_body", parentName), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Localizer.T("colgroup.ask_count_label") },
                count,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel }
                }
            }
        };
    }
}

/// <summary>Rename every child column of a group. Empty names are allowed (same as any other header).</summary>
public sealed class ColumnGroupNamesDialog : Window
{
    public ColumnGroupNamesDialog(string parentName, IReadOnlyList<string> names)
    {
        Title = Localizer.T("colgroup.ask_names_title");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        var boxes = names.Select(n => new TextBox { Text = n, MaxLength = TableLimits.MaxCellChars }).ToList();
        var ok = new Button { Content = Localizer.T("common.ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => Close(boxes.Select(b => b.Text ?? "").ToList());
        cancel.Click += (_, _) => Close(null);
        var root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Localizer.T("colgroup.ask_names_body", parentName), TextWrapping = TextWrapping.Wrap });
        foreach (var box in boxes) root.Children.Add(box);
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel }
        });
        Content = root;
    }
}
