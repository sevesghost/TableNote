using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// 2.7.0: the pop-up table of one day of the calendar. Its rows ARE the table's rows due that day (the table's columns,
/// with the due date shown as a time): what is typed here is in the table at once, with its own undo step, and what
/// changes in the table shows here. + Row adds a row to the table due that day (09:00, or the next hour today);
/// a blank time makes it all day. ⏰ sets advance reminders, 🔁 repeats it, ↗ shows the row in the table,
/// Move to moves it to another day, Remove the date takes it off the calendar (the row stays in the table).
/// ◀ ▶ go to the day before / after. Enter keeps a cell and goes down, Esc puts it back (or closes).
/// </summary>
public sealed class CalendarDayDialog : Window
{
    /// <summary>What a cell editor stands for, and the text the table had when it was drawn.</summary>
    private sealed class Edit(string row, string column, string start)
    {
        public string Row { get; } = row;
        public string Column { get; } = column;
        public string Start { get; set; } = start;
    }

    /// <summary>Column id of the time box (not a column of the table).</summary>
    internal const string TimeColumn = "\u0001time";

    private static readonly IBrush HeaderBg = CalendarWindow.Paint("#F6E08A");
    private static readonly IBrush CellBg = CalendarWindow.Paint("#FFFDF6");
    private static readonly IBrush LabelBg = CalendarWindow.Paint("#F8E7A4");
    private static readonly IBrush ActiveBg = CalendarWindow.Paint("#FFE08A");
    private static readonly IBrush OverdueBg = CalendarWindow.Paint("#FFD0C8");
    private static readonly IBrush TodayBg = CalendarWindow.Paint("#FFE9A8");
    private static readonly IBrush DoneBg = CalendarWindow.Paint("#EFEADF");
    private static readonly IBrush OkFg = CalendarWindow.Paint("#3F7A47");

    private readonly CalendarModel _model;
    private DateTime _date;
    private string? _selected;
    private readonly TextBlock _heading = new() { FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { Foreground = CalendarWindow.SoftFg, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _hint = new() { Foreground = CalendarWindow.SoftFg, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };
    private readonly StackPanel _ghostList = new() { Spacing = 2 };
    private readonly TextBlock _ghostTitle = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
    private readonly StackPanel _ghostBox;
    private readonly Button _add, _existing, _remove, _goto, _clear, _prevDay, _nextDay, _close;
    private readonly TextBlock _moveLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
    private readonly CalendarDatePicker _moveTo = new()
    {
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "dd/MM/yyyy", // CE, day/month like the rest of the table
        Width = 150,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Dictionary<string, Border> _labels = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Row, string Column), Control> _editors = new();
    private List<CalendarItem> _rows = [];
    private List<CalendarColumn> _columns = [];
    private string _signature = "";
    private bool _syncing;
    /// <summary>The table changed while a cell here was being typed in: drawn again once that cell is kept.</summary>
    private bool _pending;

    /// <summary>◀ ▶ went to another day.</summary>
    public event Action<DateTime>? DayChanged;

    public CalendarDayDialog(CalendarModel model, DateTime date, string? select = null)
    {
        _model = model;
        _date = date.Date;
        _selected = select;
        var zoom = Math.Clamp(model.Zoom(), 0.5, 3);
        Width = 1000 * zoom;
        Height = 580 * zoom;
        MinWidth = 640;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = CalendarWindow.Paint("#FFF6C2");
        FontFamily = AppFonts.Thai;
        Foreground = CalendarWindow.TextFg;

        _prevDay = CalendarWindow.Tool("◀", () => ShowDay(_date.AddDays(-1)));
        _nextDay = CalendarWindow.Tool("▶", () => ShowDay(_date.AddDays(1)));
        _prevDay.Name = "DayPrev";
        _nextDay.Name = "DayNext";
        _add = CalendarWindow.Tool("", AddRow);
        _add.Name = "DayAddRow";
        _existing = CalendarWindow.Tool("", () => _ = AddExistingAsync());
        _existing.Name = "DayAddExisting";
        _remove = CalendarWindow.Tool("", () => _ = DeleteRowAsync());
        _remove.Name = "DayDeleteRow";
        _goto = CalendarWindow.Tool("", () => { if (_selected is { } id) GoTo(id); });
        _goto.Name = "DayGoTo";
        _clear = CalendarWindow.Tool("", ClearDate);
        _clear.Name = "DayClearDate";
        _close = CalendarWindow.Tool("", Close);
        _close.Name = "DayClose";
        _close.MinWidth = 90;
        _close.HorizontalContentAlignment = HorizontalAlignment.Center;
        _moveTo.Name = "DayMoveTo";
        _moveTo.SelectedDateChanged += (_, _) =>
        {
            if (_syncing || _moveTo.SelectedDate is not { } d) return;
            MoveTo(d.Date);
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _heading.Margin = new Thickness(6, 0, 6, 0);
        _heading.Name = "DayHeading";
        Grid.SetColumn(_heading, 1);
        Grid.SetColumn(_nextDay, 2);
        _nextDay.Margin = new Thickness(0);
        top.Children.Add(_prevDay);
        top.Children.Add(_heading);
        top.Children.Add(_nextDay);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { _add, _existing, _remove, _goto, _clear, _moveLabel, _moveTo } };
        var head = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 6), Children = { top, _summary, _hint, tools, _message } };

        _ghostBox = new StackPanel { IsVisible = false, Children = { _ghostTitle, _ghostList } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(_close, 1);
        footer.Children.Add(_ghostBox);
        footer.Children.Add(_close);

        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 12), Children = { head, footer, _scroll } };
        Content = Math.Abs(zoom - 1) < 0.001 ? dock : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = dock };

        // Tunnel, so Esc and Ctrl+Z reach the pop-up before a cell's text box takes them.
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Closing += (_, _) => CommitFocused();
        ApplyTexts();
        Render();
    }

    public DateTime Date => _date;
    internal string? SelectedRow => _selected;
    internal IReadOnlyList<CalendarItem> Rows => _rows;
    internal string HeadingText => _heading.Text ?? "";
    internal string MessageText => _message.Text ?? "";
    /// <summary>The editor of a cell (column id, or <see cref="TimeColumn"/>), for tests.</summary>
    internal Control? Editor(string rowId, string columnId) => _editors.TryGetValue((rowId, columnId), out var c) ? c : null;

    private void ApplyTexts()
    {
        Title = Localizer.T("calendar.day_window");
        ToolTip.SetTip(_prevDay, Localizer.T("calendar.prev_day"));
        ToolTip.SetTip(_nextDay, Localizer.T("calendar.next_day"));
        _add.Content = Localizer.T("calendar.add_row");
        ToolTip.SetTip(_add, Localizer.T("calendar.add_row_tip"));
        _existing.Content = Localizer.T("calendar.add_existing");
        ToolTip.SetTip(_existing, Localizer.T("calendar.add_existing_tip"));
        _remove.Content = Localizer.T("calendar.delete_row");
        ToolTip.SetTip(_remove, Localizer.T("calendar.delete_row_tip"));
        _goto.Content = Localizer.T("calendar.go_to_row");
        ToolTip.SetTip(_goto, Localizer.T("calendar.go_to_row_tip"));
        _clear.Content = Localizer.T("calendar.clear_date");
        ToolTip.SetTip(_clear, Localizer.T("calendar.clear_date_tip"));
        _moveLabel.Text = Localizer.T("calendar.move_to");
        _moveTo.PlaceholderText = Localizer.T("date.placeholder");
        ToolTip.SetTip(_moveTo, Localizer.T("calendar.move_tip"));
        _close.Content = Localizer.T("detail.close");
        _hint.Text = Localizer.T("calendar.day_hint");
        _ghostTitle.Text = Localizer.T("calendar.ghosts");
    }

    /// <summary>Shows another day (◀ ▶, or the calendar asked for one), keeping what was typed.</summary>
    public void ShowDay(DateTime date, string? rowId = null)
    {
        CommitFocused();
        var changed = date.Date != _date;
        _date = date.Date;
        _selected = rowId;
        Say("");
        Render();
        if (changed) DayChanged?.Invoke(_date);
    }

    /// <summary>Draws the rows again when the table changed (not when nothing it shows changed); keeps the focused cell
    /// and anything typed into it.</summary>
    public void Refresh(bool force = false)
    {
        if (force) ApplyTexts();
        else if (Typing())
        {
            _pending = true; // never redrawn under the fingers; Commit draws it
            return;
        }
        if (!force && Signature(Load()) == _signature) { PaintSelection(); return; }
        Render();
    }

    /// <summary>A cell of the pop-up has the focus and text that is not kept yet.</summary>
    private bool Typing() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Tag: Edit edit } box && (box.Text ?? "") != edit.Start;

    private (List<CalendarItem> Rows, List<CalendarItem> Ghosts, List<CalendarColumn> Columns) Load()
    {
        if (!_model.CanShow()) return ([], [], []);
        var items = _model.Items(_date, _date.AddDays(1), false); // the day's table shows its done rows too (faded)
        return (items.Where(i => !i.Ghost).ToList(), items.Where(i => i.Ghost).ToList(), _model.Columns());
    }

    private string Signature((List<CalendarItem> Rows, List<CalendarItem> Ghosts, List<CalendarColumn> Columns) data)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)).Append(Localizer.Current);
        foreach (var c in data.Columns) sb.Append('|').Append(c.Id).Append(c.Name).Append(c.Kind).Append(string.Join(",", c.Options)).Append(c.Width);
        foreach (var i in data.Rows)
        {
            sb.Append('\n').Append(i.RowId).Append(i.Row).Append(i.Due.ToString("o")).Append(i.HasTime).Append(i.Status).Append(i.Done).Append(i.Overdue)
              .Append(i.Today).Append(i.Repeat).Append(string.Join(",", i.Advance)).Append(i.Color);
            foreach (var c in data.Columns) sb.Append('\u0002').Append(c.Kind == ColumnTypes.Days ? _model.Show(i.RowId, c.Id) : _model.Cell(i.RowId, c.Id));
        }
        foreach (var g in data.Ghosts) sb.Append("\ng").Append(g.RowId).Append(g.Due.ToString("o")).Append(g.Title);
        return sb.ToString();
    }

    private void Render()
    {
        _pending = false;
        var focus = CaptureFocus();
        var data = Load();
        _signature = Signature(data);
        _rows = data.Rows;
        _columns = data.Columns;
        if (_selected != null && _rows.All(r => r.RowId != _selected)) _selected = null;
        _selected ??= _rows.FirstOrDefault()?.RowId;
        var now = _model.Now();
        _heading.Text = CalendarOps.DayTitle(_date) + (_date == now.Date ? " · " + Localizer.T("calendar.today") : "");
        _summary.Text = CalendarOps.Summary(_rows);
        _labels.Clear();
        _editors.Clear();

        var canShow = _model.CanShow();
        _add.IsEnabled = canShow;
        _existing.IsEnabled = canShow && _model.UnplacedCount() > 0;
        if (!canShow)
        {
            _scroll.Content = EmptyText(CalendarOps.NoDueColumnText());
            _ghostBox.IsVisible = false;
            PaintSelection();
            return;
        }

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(86)));
        foreach (var c in _columns) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(c.Width)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(112)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var last = _columns.Count + 4;
        AddHeader(grid, 0, "#", null);
        AddHeader(grid, 1, Localizer.T("calendar.time"), Localizer.T("calendar.time_tip"));
        for (var c = 0; c < _columns.Count; c++) AddHeader(grid, c + 2, _columns[c].Name, null);
        AddHeader(grid, last - 2, "⏰", Localizer.T("ui.alarm_tip_none"));
        AddHeader(grid, last - 1, "🔁", Localizer.T("calendar.repeat_tip"));
        AddHeader(grid, last, "", null);

        for (var r = 0; r < _rows.Count; r++)
        {
            var item = _rows[r];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var bg = item.Color is { } hex && !item.Done ? CalendarWindow.Paint(hex)
                : item.Done ? DoneBg : item.Overdue ? OverdueBg : item.Today ? TodayBg : CellBg;
            Place(grid, RowLabel(item), r + 1, 0);
            Place(grid, TimeBox(item, bg), r + 1, 1);
            for (var c = 0; c < _columns.Count; c++) Place(grid, CellEditor(item, _columns[c], bg), r + 1, c + 2);
            Place(grid, AlarmButton(item), r + 1, last - 2);
            Place(grid, RepeatBox(item), r + 1, last - 1);
            Place(grid, GoToButton(item.RowId), r + 1, last);
        }
        _scroll.Content = _rows.Count == 0 ? new StackPanel { Children = { grid, EmptyText(Localizer.T("calendar.day_empty")) } } : grid;

        _ghostList.Children.Clear();
        foreach (var g in data.Ghosts) _ghostList.Children.Add(GhostLine(g));
        _ghostBox.IsVisible = data.Ghosts.Count > 0;
        PaintSelection();
        RestoreFocus(focus);
    }

    private static TextBlock EmptyText(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 12), Foreground = CalendarWindow.SoftFg, FontSize = 14 };

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddHeader(Grid grid, int column, string text, string? tip)
    {
        var head = new Border
        {
            Background = HeaderBg,
            BorderBrush = CalendarWindow.GridLine,
            BorderThickness = new Thickness(1),
            MinHeight = 30,
            Padding = new Thickness(6, 3),
            Child = new TextBlock { Text = text, FontWeight = FontWeight.DemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        if (tip != null) ToolTip.SetTip(head, tip);
        Place(grid, head, 0, column);
    }

    private Border RowLabel(CalendarItem item)
    {
        var label = new Border
        {
            Background = LabelBg,
            BorderBrush = CalendarWindow.GridLine,
            BorderThickness = new Thickness(1),
            MinWidth = 44,
            MinHeight = 32,
            Cursor = CalendarWindow.HandCursor,
            Child = new TextBlock
            {
                Text = (item.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTip.SetTip(label, Localizer.T("calendar.label_tip", item.Row + 1));
        label.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Select(item.RowId);
        };
        _labels[item.RowId] = label;
        return label;
    }

    private void Select(string rowId)
    {
        _selected = rowId;
        PaintSelection();
    }

    private void PaintSelection()
    {
        foreach (var (id, label) in _labels) label.Background = id == _selected ? ActiveBg : LabelBg;
        var has = _selected != null && _labels.ContainsKey(_selected);
        _remove.IsEnabled = has;
        _goto.IsEnabled = has;
        _clear.IsEnabled = has;
        _moveTo.IsEnabled = has;
    }

    private TextBox TimeBox(CalendarItem item, IBrush bg)
    {
        var text = item.HasTime ? DueDates.FormatTime(item.Due) : "";
        var box = Box(new Edit(item.RowId, TimeColumn, text), text, bg, multiline: false);
        box.PlaceholderText = Localizer.T("calendar.all_day");
        box.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(box, Localizer.T("calendar.time_tip"));
        return box;
    }

    private Control CellEditor(CalendarItem item, CalendarColumn column, IBrush bg)
    {
        var raw = _model.Cell(item.RowId, column.Id);
        var edit = new Edit(item.RowId, column.Id, raw);
        Control control;
        switch (column.Kind)
        {
            case CalendarColumn.Status:
            {
                var code = TaskStatuses.DisplayCode(raw);
                edit.Start = code;
                var combo = Combo(TaskStatuses.Codes.Select(TaskStatuses.Label).ToList(), Array.IndexOf(TaskStatuses.Codes, code), bg);
                combo.Tag = edit;
                combo.SelectionChanged += (_, _) =>
                {
                    if (_syncing || combo.SelectedIndex < 0) return;
                    var next = TaskStatuses.Codes[combo.SelectedIndex];
                    if (next == edit.Start) return;
                    Select(item.RowId);
                    if (_model.SetStatus(item.RowId, next)) edit.Start = next;
                };
                control = combo;
                break;
            }
            case ColumnTypes.Choice:
            {
                var items = new List<string> { "—" };
                items.AddRange(column.Options);
                if (raw.Length > 0 && !column.Options.Contains(raw)) items.Add(raw); // a value typed before: keep it visible
                var combo = Combo(items, raw.Length == 0 ? 0 : items.IndexOf(raw), bg);
                combo.Tag = edit;
                combo.SelectionChanged += (_, _) =>
                {
                    if (_syncing || combo.SelectedIndex < 0) return;
                    var value = combo.SelectedIndex == 0 ? "" : items[combo.SelectedIndex];
                    if (value == edit.Start) return;
                    Select(item.RowId);
                    if (_model.SetCell(item.RowId, column.Id, edit.Start, value)) edit.Start = value;
                };
                control = combo;
                break;
            }
            case ColumnTypes.Check:
            {
                var check = new CheckBox
                {
                    IsChecked = ColumnSettingsOps.IsChecked(raw),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = edit
                };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (_syncing) return;
                    Select(item.RowId);
                    var value = check.IsChecked == true ? ColumnSettingsOps.CheckedValue : "";
                    if (value != edit.Start && _model.SetCell(item.RowId, column.Id, edit.Start, value)) edit.Start = value;
                };
                _editors[(item.RowId, column.Id)] = check;
                return new Border { Background = bg, BorderBrush = CalendarWindow.GridLine, BorderThickness = new Thickness(1), MinHeight = 32, Child = check };
            }
            case ColumnTypes.Days:
            {
                var days = new Border
                {
                    Background = bg,
                    BorderBrush = CalendarWindow.GridLine,
                    BorderThickness = new Thickness(1),
                    MinHeight = 32,
                    Padding = new Thickness(6, 4),
                    Child = new TextBlock { Text = _model.Show(item.RowId, column.Id), VerticalAlignment = VerticalAlignment.Center, Foreground = CalendarWindow.SoftFg }
                };
                ToolTip.SetTip(days, Localizer.T("calendar.days_tip"));
                _editors[(item.RowId, column.Id)] = days;
                return days;
            }
            default:
            {
                var box = Box(edit, raw, bg, multiline: true);
                if (column.Kind == ColumnTypes.Number) box.TextAlignment = TextAlignment.Right;
                return box;
            }
        }
        _editors[(item.RowId, column.Id)] = control;
        return control;
    }

    private ComboBox Combo(List<string> items, int index, IBrush bg) => new()
    {
        ItemsSource = items,
        SelectedIndex = index,
        Background = bg,
        BorderBrush = CalendarWindow.GridLine,
        BorderThickness = new Thickness(1),
        MinHeight = 32,
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private TextBox Box(Edit edit, string text, IBrush bg, bool multiline)
    {
        var box = new TextBox
        {
            Text = text,
            Tag = edit,
            FontSize = 14,
            Background = bg,
            BorderBrush = CalendarWindow.GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            MinHeight = 32,
            MaxHeight = 140,
            CornerRadius = new CornerRadius(0),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.GotFocus += (_, _) => { if (_selected != edit.Row) Select(edit.Row); };
        box.LostFocus += (_, _) => Commit(box);
        box.TextChanged += (_, _) =>
        {
            var value = box.Text ?? "";
            if (value.Length > TableLimits.MaxCellChars)
            {
                box.Text = TableLimits.Clip(value, TableLimits.MaxCellChars); // raises TextChanged again with the cut text
                box.CaretIndex = (box.Text ?? "").Length;
                Say(TableLimits.CellTooLongMessage, error: true);
                return;
            }
            // Like the table's own cells: what is typed is in the table (and saved) at once; the undo step comes when the
            // cell is kept. The time is read when it is kept (a half-typed "9:" is no time yet).
            if (edit.Column != TimeColumn && box.Parent != null) _model.TypeCell(edit.Row, edit.Column, value);
        };
        // Enter keeps the cell and goes to the row below (Alt+Enter: a new line), like the table.
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Return || e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (!multiline) return;
                e.Handled = true;
                var t = box.Text ?? "";
                var a = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, t.Length);
                var b = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), a, t.Length);
                box.Text = t[..a] + "\n" + t[b..];
                box.SelectionStart = box.SelectionEnd = box.CaretIndex = a + 1;
                return;
            }
            e.Handled = true;
            var row = edit.Row;
            var column = edit.Column;
            Commit(box);
            var at = _rows.FindIndex(r => r.RowId == row) + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            if (at >= 0 && at < _rows.Count) FocusEditor(_rows[at].RowId, column);
        }, RoutingStrategies.Tunnel);
        _editors[(edit.Row, edit.Column)] = box;
        return box;
    }

    private Button AlarmButton(CalendarItem item)
    {
        var button = CalendarWindow.Tool(item.Advance.Count > 0 ? "⏰" + item.Advance.Count : "⏰", () => { });
        button.Margin = new Thickness(2);
        button.Padding = new Thickness(6, 2);
        ToolTip.SetTip(button, item.Advance.Count > 0
            ? Localizer.T("ui.alarm_tip_offsets", string.Join(", ", item.Advance.Select(o => Localizer.T("remind.before", ReminderPlan.OffsetLabel(o)))))
            : Localizer.T("ui.alarm_tip_none"));
        button.Click += async (_, _) =>
        {
            CommitFocused();
            Select(item.RowId);
            await _model.EditReminders(item.RowId, this);
            Refresh(force: true);
        };
        _editors[(item.RowId, "\u0001alarm")] = button;
        return button;
    }

    private ComboBox RepeatBox(CalendarItem item)
    {
        var combo = Combo(Recurrence.Labels.ToList(), Recurrence.IndexOf(item.Repeat), CellBg);
        combo.FontSize = 12;
        ToolTip.SetTip(combo, Localizer.T("calendar.repeat_tip"));
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0 || combo.SelectedIndex == Recurrence.IndexOf(item.Repeat)) return;
            Select(item.RowId);
            _model.SetRepeat(item.RowId, combo.SelectedIndex);
        };
        _editors[(item.RowId, "\u0001repeat")] = combo;
        return combo;
    }

    private Button GoToButton(string rowId)
    {
        var button = CalendarWindow.Tool("↗", () => GoTo(rowId));
        button.Margin = new Thickness(2);
        button.Padding = new Thickness(8, 2);
        ToolTip.SetTip(button, Localizer.T("calendar.go_to_row_tip"));
        return button;
    }

    private Control GhostLine(CalendarItem g)
    {
        var text = new TextBlock
        {
            Text = "🔁 " + CalendarOps.TimeText(g) + "  " + CalendarOps.TitleText(g) + " — " + Localizer.T("calendar.tip_ghost", CalendarOps.RepeatText(g.Repeat), g.Row + 1),
            Foreground = CalendarWindow.SoftFg,
            FontStyle = FontStyle.Italic,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var go = CalendarWindow.Tool("↗", () => GoTo(g.RowId));
        go.Padding = new Thickness(6, 1);
        ToolTip.SetTip(go, Localizer.T("calendar.go_to_row_tip"));
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        line.Children.Add(go);
        line.Children.Add(text);
        ToolTip.SetTip(line, CalendarOps.Describe(g));
        return line;
    }

    // ───────────────────────── actions ─────────────────────────

    private void Commit(TextBox box)
    {
        if (box.Tag is not Edit edit) return;
        var text = box.Text ?? "";
        if (text == edit.Start)
        {
            if (_pending) Dispatcher.UIThread.Post(() => { if (_pending) Refresh(); });
            return;
        }
        CommitCore(box, edit, text);
        if (_pending) Dispatcher.UIThread.Post(() => { if (_pending) Refresh(); });
    }

    private void CommitCore(TextBox box, Edit edit, string text)
    {
        if (edit.Column == TimeColumn)
        {
            if (!CalendarOps.TryParseTime(text, out var time))
            {
                box.Text = edit.Start;
                Say(Localizer.T("date.bad_time", text.Trim()), error: true);
                return;
            }
            var shown = time is { } t ? DueDates.FormatTime(DateTime.Today + t) : "";
            if (_model.SetTime(edit.Row, time)) Say("");
            edit.Start = shown;
            box.Text = shown;
            return;
        }
        if (_model.SetCell(edit.Row, edit.Column, edit.Start, text))
        {
            edit.Start = text;
            Say("");
        }
        else
        {
            box.Text = edit.Start;
            Say(Localizer.T("calendar.not_saved"), error: true);
        }
    }

    private void CommitFocused()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Tag: Edit } box) Commit(box);
    }

    private void AddRow()
    {
        CommitFocused();
        var id = _model.AddRow(_date);
        if (id == null)
        {
            Say(_model.CanShow() ? Localizer.T("limit.row_limit", TableLimits.MaxRows) : CalendarOps.NoDueColumnText(), error: true);
            return;
        }
        _selected = id;
        Render();
        var first = _columns.FirstOrDefault(c => c.Kind is ColumnTypes.Text or ColumnTypes.Number);
        FocusEditor(id, first?.Id ?? TimeColumn);
        Say(Localizer.T("calendar.added_here", CalendarOps.ShortDate(_date)), error: false);
    }

    /// <summary>"Put existing rows on the calendar", on this day; the rows put here are shown and the first selected.</summary>
    private async Task AddExistingAsync()
    {
        CommitFocused();
        var dialog = new CalendarPlaceDialog(_model, _date);
        Placing = dialog;
        try { await dialog.ShowDialog(this); }
        finally { Placing = null; }
        if (dialog.Placed == 0) return;
        Refresh(force: true);
        Say(Localizer.T(dialog.Placed == 1 ? "calendar.placed_one" : "calendar.placed", dialog.Placed), error: false);
    }

    /// <summary>"Put existing rows on the calendar" while it is open over the pop-up (tests).</summary>
    internal CalendarPlaceDialog? Placing { get; private set; }

    private async Task DeleteRowAsync()
    {
        if (_selected is not { } id) return;
        CommitFocused();
        if (!await _model.Delete(id, this)) return;
        _selected = null;
        Render();
        Say(Localizer.T("calendar.deleted"), error: false);
    }

    private void MoveTo(DateTime date)
    {
        _syncing = true;
        _moveTo.SelectedDate = null;
        _syncing = false;
        if (_selected is not { } id) return;
        CommitFocused();
        if (date == _date) return;
        if (!_model.Move(id, date)) return;
        Render();
        Say(Localizer.T("calendar.moved", CalendarOps.ShortDate(date)), error: false);
    }

    private void ClearDate()
    {
        if (_selected is not { } id) return;
        CommitFocused();
        if (!_model.ClearDate(id)) return;
        Render();
        Say(Localizer.T("calendar.cleared"), error: false);
    }

    private void GoTo(string rowId)
    {
        CommitFocused();
        _model.GoToRow(rowId);
        Close();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // A cell being typed in: Esc puts its text back first; otherwise it closes the pop-up.
            if (focused is TextBox { Tag: Edit edit } box && (box.Text ?? "") != edit.Start) { box.Text = edit.Start; box.CaretIndex = edit.Start.Length; return; }
            Close();
            return;
        }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var redo = e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        if (e.Key != Key.Z && !redo) return;
        // The letters being typed in a cell are undone by the text box itself (as in the table).
        if (focused is TextBox { Tag: Edit typing } t && (t.Text ?? "") != typing.Start) return;
        e.Handled = true;
        var label = redo ? _model.Redo() : _model.Undo();
        if (label != null)
        {
            Refresh(force: true);
            Say(Localizer.T(redo ? "ui.redo_done" : "ui.undo_done", label), error: false);
        }
    }

    private void Say(string text, bool error = false)
    {
        _message.Text = text;
        _message.Foreground = error ? CalendarWindow.SundayFg : OkFg;
    }

    private void FocusEditor(string rowId, string columnId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_editors.TryGetValue((rowId, columnId), out var control)) return;
            control.BringIntoView();
            control.Focus(NavigationMethod.Tab);
            if (control is TextBox tb) tb.CaretIndex = (tb.Text ?? "").Length;
        }, DispatcherPriority.Loaded);
    }

    private (string Row, string Column, string? Text, string? Start, int Caret)? CaptureFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox { Tag: Edit e } box) return (e.Row, e.Column, box.Text ?? "", e.Start, box.CaretIndex);
        if (focused is Control { Tag: Edit other }) return (other.Row, other.Column, null, null, 0);
        return null;
    }

    private void RestoreFocus((string Row, string Column, string? Text, string? Start, int Caret)? focus)
    {
        if (focus is not { } f || !_editors.TryGetValue((f.Row, f.Column), out var control)) return;
        if (control is TextBox box && f.Text != null && f.Text != f.Start)
            box.Text = f.Text; // typing that was not kept yet stays (its Start is the table's text)
        void Put()
        {
            control.Focus(NavigationMethod.Unspecified);
            if (control is TextBox tb) tb.CaretIndex = Math.Min(f.Caret, (tb.Text ?? "").Length);
        }
        Put(); // at once, so no key is lost; again once it is laid out (a control just made may not take it yet)
        Dispatcher.UIThread.Post(() => { if (!control.IsFocused) Put(); }, DispatcherPriority.Loaded);
    }
}
