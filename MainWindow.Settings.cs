using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TableNote;

/// <summary>v2.4.0: column settings (name, type, width, drop-down options) and row colour / highlight.
/// Settings live in NoteDocument.ColumnSettings (by column id) and RowStyles (by row id); every change is one
/// undo step (SnapshotAction) and is saved at once (FlushSave).</summary>
public partial class MainWindow
{
    private static readonly IBrush InvalidNumberFg = new SolidColorBrush(Color.Parse("#B03A2E"));

    /// <summary>Called once from the constructor: registers "Column settings…" in the column header menu.</summary>
    private void InitSettings()
    {
        _extraColumnMenuItems = column => [BuildColumnSettingsMenuItem(column)];
        _extraRowMenuItems = BuildRowStyleMenuItems;
    }

    // ───────────────────────── columns ─────────────────────────

    /// <summary>"Column settings…" item for the header right-click menu.</summary>
    public MenuItem BuildColumnSettingsMenuItem(int column)
    {
        var type = ColumnSettingsOps.TypeOf(_session.Doc, column);
        var item = new MenuItem { Header = Localizer.T("colset.menu"), IsEnabled = _view == ViewMode.Edit };
        ToolTip.SetTip(item, Localizer.T("colset.type_now", ColumnTypes.Label(type)));
        item.Click += (_, _) => OpenColumnSettings(column);
        return item;
    }

    /// <summary>Open the column settings dialog for a column (by index); result is one undo step.</summary>
    public void OpenColumnSettings(int column) =>
        _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.column_settings"), () => ColumnSettingsAsync(column)));

    private async Task ColumnSettingsAsync(int column)
    {
        var doc = _session.Doc;
        if (column < 0 || column >= doc.Columns.Count || _view != ViewMode.Edit) return;
        var id = doc.ColumnIdAt(column);
        var current = ColumnSettingsOps.Get(doc, column);
        var system = ColumnSettingsOps.IsSystemColumn(doc, column);
        _busy = true;
        try
        {
            var dlg = new ColumnSettingsDialog(doc.Columns[column], current.Type, doc.ColumnWidth(column), current.Options, system);
            var res = await dlg.ShowDialog<ColumnSettingsDialog.Result?>(this);
            if (res == null) return;
            column = doc.ColumnIds.IndexOf(id); // the table may have been rebuilt meanwhile → find the column again
            if (column < 0) { StatusText.Text = Localizer.T("colset.gone"); return; }
            if (!ColumnSettingsOps.Apply(doc, column, res.Name, res.Type, res.Width, res.Options)) return;
            Rebuild(); FlushSave();
            StatusText.Text = Localizer.T("colset.done", doc.Columns[column], ColumnTypes.Label(ColumnSettingsOps.TypeOf(doc, column)));
            AppLog.Write("table", $"column settings {column + 1}: {ColumnSettingsOps.TypeOf(doc, column)}");
        }
        finally { _busy = false; }
    }

    /// <summary>Editor for a data cell of a typed column; null = plain text (the existing text box).</summary>
    private Control? MakeTypedCell(int row, int column, IBrush bg)
    {
        var setting = ColumnSettingsOps.Get(_session.Doc, column);
        switch (setting.Type)
        {
            case ColumnTypes.Number:
            {
                var box = MakeTextBox(_session.Doc.GetCell(row, column), false, row, column, bg);
                box.TextAlignment = TextAlignment.Right;
                var normalFg = box.Foreground;
                void Check()
                {
                    var ok = ColumnSettingsOps.IsNumber(box.Text);
                    box.Foreground = ok ? normalFg : InvalidNumberFg;
                    ToolTip.SetTip(box, ok ? null : Localizer.T("colset.not_number"));
                }
                Check();
                box.TextChanged += (_, _) => Check();
                return box;
            }
            case ColumnTypes.Choice: return MakeChoiceCell(row, column, bg, setting.Options);
            case ColumnTypes.Check: return MakeCheckCell(row, column, bg);
            default: return null;
        }
    }

    private const string BlankChoice = "—";

    private Control MakeChoiceCell(int row, int column, IBrush bg, List<string> options)
    {
        var raw = _session.Doc.GetCell(row, column);
        var items = new List<string> { BlankChoice };
        items.AddRange(options);
        if (raw.Length > 0 && !options.Contains(raw)) items.Add(raw); // value typed before / not in the list: keep it visible
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = raw.Length == 0 ? 0 : items.IndexOf(raw),
            FontFamily = AppFonts.Thai, FontSize = 14,
            Background = bg, BorderBrush = GridLine, BorderThickness = new Thickness(1),
            MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = _view == ViewMode.Edit, IsHitTestVisible = _view == ViewMode.Edit
        };
        if (options.Count == 0) ToolTip.SetTip(combo, Localizer.T("colset.no_options"));
        combo.PointerPressed += (_, e) =>
        {
            if (_view == ViewMode.Background) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _session.ExtendTo(row, column);
            else _session.ChooseCell(row, column);
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_ignoreText || combo.SelectedIndex < 0) return;
            var value = combo.SelectedIndex == 0 ? "" : items[combo.SelectedIndex];
            SetTypedCell(row, column, value, Localizer.T("undo.choose_value"));
        };
        AttachEnterNavigation(combo, row, column, skip: () => combo.IsDropDownOpen);
        return combo;
    }

    private Control MakeCheckCell(int row, int column, IBrush bg)
    {
        var raw = _session.Doc.GetCell(row, column);
        var check = new CheckBox
        {
            IsChecked = ColumnSettingsOps.IsChecked(raw),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = _view == ViewMode.Edit, IsHitTestVisible = _view == ViewMode.Edit
        };
        // Text that is neither blank nor a "ticked" word stays in the file; show it so it isn't silently hidden.
        if (raw.Trim().Length > 0 && !ColumnSettingsOps.IsChecked(raw)) ToolTip.SetTip(check, Localizer.T("colset.old_value", raw));
        check.IsCheckedChanged += (_, _) =>
        {
            if (_ignoreText) return;
            _session.ChooseCell(row, column);
            SetTypedCell(row, column, check.IsChecked == true ? ColumnSettingsOps.CheckedValue : "", Localizer.T("undo.toggle_check"));
        };
        var cell = new Border
        {
            Background = bg, BorderBrush = GridLine, BorderThickness = new Thickness(1), MinHeight = 32, MinWidth = 96,
            Child = check
        };
        AttachEnterNavigation(check, row, column);
        return cell;
    }

    /// <summary>Write one typed cell with a normal single-cell undo step.</summary>
    private void SetTypedCell(int row, int column, string value, string label)
    {
        var prev = _session.Doc.GetCell(row, column);
        if (prev == value || !_session.SetCell(row, column, value)) return;
        _undo.Push(CellEditAction.For(_session.Doc, _session.Doc.RowIdAt(row), column, prev, value, label));
        ScheduleSave();
        _reminders.CheckNow();
    }

    // ───────────────────────── rows ─────────────────────────

    private static readonly IBrush HighlightLabelBg = new SolidColorBrush(Color.Parse("#F2B35E"));
    private static readonly Dictionary<string, IBrush> RowColourBrushes = new(StringComparer.Ordinal);

    /// <summary>User row style on top of the status colours (done / overdue / due today):
    /// a row colour replaces the cell background; a highlighted row gets an orange row-number label.
    /// The active-row marker (LabelBrushFor) still wins over both.</summary>
    private (IBrush label, IBrush cell) WithRowStyle(int row, (IBrush label, IBrush cell) status)
    {
        if (row < 0 || row >= _session.Doc.Rows.Count || _session.Doc.RowStyles.Count == 0) return status;
        var style = RowStyleOps.Get(_session.Doc, _session.Doc.RowIdAt(row));
        var cell = status.cell;
        if (RowStyleOps.HexFor(style.Color) is { } hex)
        {
            // cached so the minute colour check (ReferenceEquals on the label) and repaints reuse one brush
            if (!RowColourBrushes.TryGetValue(hex, out var b)) RowColourBrushes[hex] = b = new SolidColorBrush(Color.Parse(hex));
            cell = b;
        }
        return (style.Highlight ? HighlightLabelBg : status.label, cell);
    }

    /// <summary>Items for the row-number right-click menu (wired in MainWindow: <c>_extraRowMenuItems = BuildRowStyleMenuItems;</c>): colour submenu (with "None"), highlight toggle, clear.</summary>
    public IEnumerable<Control> BuildRowStyleMenuItems(string rowId)
    {
        var style = RowStyleOps.Get(_session.Doc, rowId);
        var edit = _view == ViewMode.Edit;
        var colours = new List<Control>();
        var none = new MenuItem { Header = Localizer.T("rowstyle.none"), ToggleType = MenuItemToggleType.Radio, IsChecked = style.Color == null };
        none.Click += (_, _) => ChangeRowStyle(rowId, Localizer.T("undo.row_no_color"), d => RowStyleOps.SetColor(d, rowId, null));
        colours.Add(none);
        colours.Add(new Separator());
        foreach (var (key, label, hex) in RowStyleOps.Palette)
        {
            var k = key;
            var m = new MenuItem
            {
                Header = Localizer.T("colour." + key),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(style.Color, key, StringComparison.OrdinalIgnoreCase),
                Icon = new Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.Parse(hex)), BorderBrush = GridLine, BorderThickness = new Thickness(1)
                }
            };
            m.Click += (_, _) => ChangeRowStyle(rowId, Localizer.T("undo.row_color", Localizer.T("colour." + k)), d => RowStyleOps.SetColor(d, rowId, k));
            colours.Add(m);
        }
        var colourMenu = new MenuItem { Header = Localizer.T("rowstyle.colour"), ItemsSource = colours, IsEnabled = edit };
        var highlight = new MenuItem
        {
            Header = Localizer.T("rowstyle.highlight"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = style.Highlight, IsEnabled = edit
        };
        highlight.Click += (_, _) => ChangeRowStyle(rowId, Localizer.T(style.Highlight ? "undo.unhighlight_row" : "undo.highlight_row"),
            d => RowStyleOps.SetHighlight(d, rowId, !style.Highlight));
        var clear = new MenuItem { Header = Localizer.T("rowstyle.clear"), IsEnabled = edit && (style.Color != null || style.Highlight) };
        clear.Click += (_, _) => ChangeRowStyle(rowId, Localizer.T("undo.clear_row_style"), d => RowStyleOps.Clear(d, rowId));
        return new List<Control> { colourMenu, highlight, clear };
    }

    /// <summary>One undo step per style change; repaints just that row and saves at once.</summary>
    private void ChangeRowStyle(string rowId, string label, Func<NoteDocument, bool> change)
    {
        if (_view != ViewMode.Edit) return;
        CommitFocusedEdit();
        var before = Snapshot.CloneLite(_session.Doc);
        if (!change(_session.Doc)) return;
        _undo.Push(new SnapshotAction(label, before, Snapshot.CloneLite(_session.Doc)));
        var row = FindRowById(rowId);
        if (row >= 0) Dispatcher.UIThread.Post(() => RefreshRow(row)); // after the menu closes
        FlushSave();
        StatusText.Text = label;
    }
}

/// <summary>v2.4.0: "Column settings" dialog — name, type, width, drop-down options (one per line).</summary>
public sealed class ColumnSettingsDialog : Window
{
    public sealed record Result(string Name, string Type, int Width, List<string> Options);

    public ColumnSettingsDialog(string name, string type, int width, IEnumerable<string> options, bool systemColumn)
    {
        Title = Localizer.T("colset.title");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));

        var nameBox = new TextBox { Text = name, MaxLength = TableLimits.MaxCellChars };
        var typeBox = new ComboBox
        {
            ItemsSource = ColumnTypes.All.Select(ColumnTypes.Label).ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(ColumnTypes.All, ColumnTypes.Normalize(type))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !systemColumn
        };
        var widthBox = new NumericUpDown
        {
            Minimum = TableLimits.MinColumnWidth, Maximum = TableLimits.MaxColumnWidth, Increment = 10,
            Value = width, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left
        };
        var optionsBox = new TextBox
        {
            Text = string.Join("\n", options), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            Height = 120, PlaceholderText = Localizer.T("colset.options_ph")
        };
        var optionsLabel = new TextBlock { Text = Localizer.T("colset.options", ColumnSettingsOps.MaxOptions) };
        var optionsPanel = new StackPanel { Spacing = 4, Children = { optionsLabel, optionsBox } };
        void SyncOptions() => optionsPanel.IsVisible = typeBox.SelectedIndex == Array.IndexOf(ColumnTypes.All, ColumnTypes.Choice);
        typeBox.SelectionChanged += (_, _) => SyncOptions();
        SyncOptions();

        var ok = new Button { Content = Localizer.T("common.ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var t = ColumnTypes.All[Math.Clamp(typeBox.SelectedIndex, 0, ColumnTypes.All.Length - 1)];
            var opts = ColumnSettingsOps.CleanOptions((optionsBox.Text ?? "").Split('\n'));
            Close(new Result(nameBox.Text ?? "", t, (int)(widthBox.Value ?? width), opts));
        };
        cancel.Click += (_, _) => Close(null);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Localizer.T("colset.name") });
        root.Children.Add(nameBox);
        root.Children.Add(new TextBlock { Text = Localizer.T("colset.type"), Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(typeBox);
        root.Children.Add(new TextBlock
        {
            Text = systemColumn
                ? Localizer.T("colset.system_note")
                : Localizer.T("colset.type_note"),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12
        });
        root.Children.Add(new TextBlock { Text = Localizer.T("colset.width"), Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(widthBox);
        root.Children.Add(optionsPanel);
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel }
        });
        Content = root;
    }
}
