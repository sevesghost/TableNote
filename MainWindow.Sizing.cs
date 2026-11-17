using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// v2.6: Excel-style Column width… and Row height… dialogs. One apply is one undo step (SnapshotAction).
/// Width is the stored pixel width at 100% zoom. A collapsed column (feature 2.1) cannot be edited here,
/// and a multi-column apply never writes its stored width.
/// </summary>
public partial class MainWindow
{
    private int _sizeAnchorRow, _sizeActiveRow, _sizeAnchorCol, _sizeActiveCol;
    private bool _sizeHadSelection;

    /// <summary>Called once from the constructor, after the other menu extensions are registered.</summary>
    private void InitSizing()
    {
        var prevCol = _extraColumnMenuItems;
        _extraColumnMenuItems = column =>
        {
            var items = prevCol?.Invoke(column)?.ToList() ?? [];
            items.Add(BuildColumnWidthMenuItem(column));
            return items;
        };
        var prevRow = _extraRowMenuItems;
        _extraRowMenuItems = rowId =>
        {
            var items = prevRow?.Invoke(rowId)?.ToList() ?? [];
            items.Add(BuildRowHeightMenuItem(rowId));
            return items;
        };
    }

    /// <summary>Hook from the row menu, before it moves the active row onto the clicked row.</summary>
    private void NoteSizingSelection()
    {
        _sizeAnchorRow = _session.AnchorRow;
        _sizeActiveRow = _session.ActiveRow;
        _sizeAnchorCol = _session.AnchorCol;
        _sizeActiveCol = _session.ActiveCol;
        _sizeHadSelection = _session.HasSelection;
    }

    public MenuItem BuildColumnWidthMenuItem(int column)
    {
        var hidden = SizeSpec.IsHiddenColumn(_session.Doc, column);
        var item = new MenuItem
        {
            Header = Localizer.T("size.col_menu"),
            IsEnabled = MenuCommandRules.ColumnWidth(_view == ViewMode.Edit, column, _session.Doc.Columns.Count, hidden)
        };
        if (hidden) ToolTip.SetTip(item, Localizer.T("size.hidden_column"));
        item.Click += (_, _) => OpenColumnWidth(column);
        return item;
    }

    public MenuItem BuildRowHeightMenuItem(string rowId)
    {
        var hasRow = !string.IsNullOrEmpty(rowId) && _session.Doc.RowIds.Contains(rowId);
        var item = new MenuItem
        {
            Header = Localizer.T("size.row_menu"),
            IsEnabled = MenuCommandRules.RowHeight(_view == ViewMode.Edit, hasRow)
        };
        item.Click += (_, _) => OpenRowHeight(rowId);
        return item;
    }

    public void OpenColumnWidth(int column) => _ = RunStructureAsync(() => ColumnWidthAsync(column));

    public void OpenRowHeight(string rowId) => _ = RunStructureAsync(() => RowHeightAsync(rowId));

    private async Task ColumnWidthAsync(int column)
    {
        var doc = _session.Doc;
        if (_view != ViewMode.Edit || column < 0 || column >= doc.Columns.Count) return;
        if (SizeSpec.IsHiddenColumn(doc, column)) { StatusText.Text = Localizer.T("size.hidden_column"); return; }
        var targets = SizeSpec.TargetColumns(doc, column, _session.AnchorCol, _session.ActiveCol, _session.HasSelection);
        if (targets.Count == 0) return;
        var ids = targets.Select(doc.ColumnIdAt).ToList();
        var prefill = SizeSpec.Describe(targets.Select(c => (int?)SizeSpec.StoredColumnWidth(doc, c)).ToList());
        var before = Snapshot.CloneLite(doc);
        var beforeJson = Snapshot.Serialize(before);
        _busy = true;
        try
        {
            var dlg = new SizeDialog(row: false, prefill, TableLimits.MinColumnWidth, TableLimits.MaxColumnWidth, targets.Count);
            var res = await dlg.ShowDialog<SizeDialog.Result?>(this);
            if (res == null) return;
            var cols = ids.Select(id => doc.ColumnIds.IndexOf(id)).Where(i => i >= 0 && !SizeSpec.IsHiddenColumn(doc, i)).ToList();
            if (cols.Count == 0) { StatusText.Text = Localizer.T("size.col_gone"); return; }
            var changed = res.UseDefault
                ? SizeSpec.ResetColumnWidths(doc, cols)
                : SizeSpec.ApplyColumnWidths(doc, cols, res.Value);
            if (!changed) { StatusText.Text = Localizer.T("size.unchanged"); return; }
            Rebuild();
            FlushSave();
            PushSizeUndo(before, beforeJson, Localizer.T(res.UseDefault ? "undo.reset_column_width" : "undo.column_width"));
            StatusText.Text = res.UseDefault
                ? Localizer.T("size.col_reset", TableLimits.DefaultColumnWidth)
                : Localizer.T("size.col_done", res.Value);
        }
        finally { _busy = false; }
    }

    private async Task RowHeightAsync(string rowId)
    {
        var doc = _session.Doc;
        if (_view != ViewMode.Edit) return;
        var clicked = doc.RowIds.IndexOf(rowId);
        if (clicked < 0) return;
        var targets = SizeSpec.TargetRows(doc, clicked, _sizeAnchorRow, _sizeActiveRow, _sizeHadSelection);
        if (targets.Count == 0) targets = [clicked];
        var ids = targets.Select(doc.RowIdAt).ToList();
        var prefill = SizeSpec.Describe(targets.Select(r => SizeSpec.StoredRowHeight(doc, r)).ToList());
        var before = Snapshot.CloneLite(doc);
        var beforeJson = Snapshot.Serialize(before);
        _busy = true;
        try
        {
            var dlg = new SizeDialog(row: true, prefill, SizeSpec.MinRowHeight, SizeSpec.MaxRowHeight, targets.Count);
            var res = await dlg.ShowDialog<SizeDialog.Result?>(this);
            if (res == null) return;
            var rows = ids.Select(id => doc.RowIds.IndexOf(id)).Where(i => i >= 0).ToList();
            if (rows.Count == 0) { StatusText.Text = Localizer.T("size.row_gone"); return; }
            var changed = res.UseDefault
                ? SizeSpec.ApplyRowHeights(doc, rows, null)
                : SizeSpec.ApplyRowHeights(doc, rows, res.Value);
            if (!changed) { StatusText.Text = Localizer.T("size.unchanged"); return; }
            Rebuild();
            FlushSave();
            PushSizeUndo(before, beforeJson, Localizer.T(res.UseDefault ? "undo.reset_row_height" : "undo.row_height"));
            StatusText.Text = res.UseDefault ? Localizer.T("size.row_reset") : Localizer.T("size.row_done", res.Value);
        }
        finally { _busy = false; }
    }

    private void PushSizeUndo(NoteDocument before, string beforeJson, string label)
    {
        var after = Snapshot.CloneLite(_session.Doc);
        if (Snapshot.Serialize(after) != beforeJson)
            _undo.Push(new SnapshotAction(label, before, after));
    }

    /// <summary>A stored height fixes the row and clips overflow. The full cell text stays in a tooltip. Auto does nothing.</summary>
    private void ApplySavedRowHeight(Grid grid, int rowIndex)
    {
        if (SizeSpec.StoredRowHeight(_session.Doc, rowIndex) is not int px) return;
        grid.Height = px;
        grid.MinHeight = px;
        grid.MaxHeight = px;
        grid.ClipToBounds = true;
        foreach (var child in grid.Children.OfType<Control>())
        {
            var col = Grid.GetColumn(child) - 1;
            if (col < 0) continue;
            child.MaxHeight = px;
            var text = _session.Doc.GetCell(rowIndex, col);
            if (text.Length > 0) ToolTip.SetTip(child, text);
        }
    }
}

/// <summary>Small width / height dialog. Enter confirms when the number is valid, Esc cancels. OK stays disabled until then.</summary>
public sealed class SizeDialog : Window
{
    /// <summary><paramref name="UseDefault"/> is reset-to-140 for a column, or Auto for a row.</summary>
    public sealed record Result(bool UseDefault, int Value);

    private readonly TextBox _input;
    private readonly TextBlock _error;
    private readonly Button _ok;
    private readonly bool _row;
    private readonly int _min, _max;
    private readonly CheckBox? _auto;

    public SizeDialog(bool row, SizeSpec.Prefill prefill, int min, int max, int count)
    {
        _row = row;
        _min = min;
        _max = max;
        _auto = null;
        Title = Localizer.T(row ? "size.row_label" : "size.col_label");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));

        var caption = prefill.Mixed
            ? Localizer.T("size.multiple")
            : row && prefill.Value is null
                ? Localizer.T("size.current_auto")
                : Localizer.T(row ? "size.current_row" : "size.current_col", prefill.Value ?? 0);
        _input = new TextBox
        {
            Text = prefill.Mixed || prefill.Value is null ? "" : prefill.Value.Value.ToString(CultureInfo.InvariantCulture),
            PlaceholderText = prefill.Mixed ? Localizer.T("size.multiple") : "",
            FontSize = 20,
            FontFamily = AppFonts.Thai,
            Foreground = new SolidColorBrush(Color.Parse("#3B3222")),
            Background = new SolidColorBrush(Color.Parse("#FFFDF6")),
            MinHeight = 36,
            Padding = new Thickness(8, 4)
        };
        _error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#B03A2E")), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var range = new TextBlock
        {
            Text = Localizer.T("size.range", min, max),
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var applies = new TextBlock
        {
            Text = count <= 1
                ? Localizer.T(row ? "size.applies_one_row" : "size.applies_one_col")
                : Localizer.T(row ? "size.applies_rows" : "size.applies_cols", count),
            Opacity = 0.75,
            FontSize = 12
        };
        _ok = new Button { Content = Localizer.T("common.ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        _ok.Click += (_, _) => Accept(auto: _auto?.IsChecked == true);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 10, 0, 0)
        };
        if (!row)
        {
            var reset = new Button { Content = Localizer.T("size.reset"), MinWidth = 120 };
            reset.Click += (_, _) => Close(new Result(true, TableLimits.DefaultColumnWidth));
            Grid.SetColumn(reset, 0);
            buttons.Children.Add(reset);
        }
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Children = { _ok, cancel } };
        Grid.SetColumn(pair, 2);
        buttons.Children.Add(pair);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Localizer.T(row ? "size.row_label" : "size.col_label"), FontWeight = FontWeight.SemiBold });
        root.Children.Add(new TextBlock { Text = caption, FontSize = 16, FontWeight = FontWeight.Bold });
        if (row)
        {
            _auto = new CheckBox
            {
                Content = Localizer.T("size.auto"),
                IsChecked = !prefill.Mixed && prefill.Value is null,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _auto.IsCheckedChanged += (_, _) => Refresh();
            root.Children.Add(_auto);
        }
        root.Children.Add(_input);
        root.Children.Add(range);
        root.Children.Add(applies);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;

        _input.TextChanged += (_, _) => Refresh();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; if (_ok.IsEnabled) Accept(auto: _auto?.IsChecked == true); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
        };
        Opened += (_, _) =>
        {
            if (_auto?.IsChecked == true) _auto.Focus();
            else { _input.Focus(); _input.SelectAll(); }
        };
        Refresh();
    }

    /// <summary>What the number box is showing (empty when the selection disagreed or the row is auto).</summary>
    public string InputText => _input.Text ?? "";

    private void Refresh()
    {
        var auto = _auto?.IsChecked == true;
        _input.IsEnabled = !auto;
        var message = auto ? null : SizeSpec.ValidationMessage(_input.Text, _min, _max);
        _error.Text = message ?? "";
        _error.IsVisible = message != null;
        _ok.IsEnabled = SizeSpec.CanApply(_input.Text, _min, _max, auto);
    }

    private void Accept(bool auto)
    {
        if (auto) { Close(new Result(true, 0)); return; }
        if (!SizeSpec.IsAccepted(_input.Text, _min, _max, out var value)) { Refresh(); return; }
        Close(new Result(false, value));
    }
}
