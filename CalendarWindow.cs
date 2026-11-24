using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>2.7.0: a column of the table as the day pop-up shows it. <see cref="Kind"/> is a <see cref="ColumnTypes"/>
/// value or <see cref="CalendarColumn.Status"/>.</summary>
public sealed record CalendarColumn(string Id, string Name, string Kind, IReadOnlyList<string> Options, double Width)
{
    public const string Status = "status";
}

/// <summary>
/// 2.7.0: what the calendar window and the day pop-up may do with the table. Every call goes to the live table
/// (MainWindow), with an undo step, a save and a reminder check, so the calendar and the table always show the same
/// rows. <see cref="Changed"/> fires (debounced) after the table changed, wherever the change came from.
/// </summary>
public sealed class CalendarModel
{
    /// <summary>Items due from the first date up to (not including) the second; true leaves out the done rows.</summary>
    public required Func<DateTime, DateTime, bool, List<CalendarItem>> Items { get; init; }
    public required Func<bool> CanShow { get; init; }
    public required Func<List<CalendarColumn>> Columns { get; init; }
    /// <summary>The stored text of a cell (row id, column id).</summary>
    public required Func<string, string, string> Cell { get; init; }
    /// <summary>The text a read-only cell shows (a days counter: "150 days").</summary>
    public required Func<string, string, string> Show { get; init; }
    /// <summary>A cell is kept (Enter, Tab, another cell): row id, column id, the text it had before, the new text.
    /// One undo step of the table.</summary>
    public required Func<string, string, string, string, bool> SetCell { get; init; }
    /// <summary>While typing: the text goes into the table at once (saved like the table's own typing), no undo step yet.</summary>
    public required Func<string, string, string, bool> TypeCell { get; init; }
    /// <summary>Adds a row due on the day; returns its id, or null (table full, no due column).</summary>
    public required Func<DateTime, string?> AddRow { get; init; }
    public required Func<string, TimeSpan?, bool> SetTime { get; init; }
    public required Func<string, DateTime, bool> Move { get; init; }
    public required Func<string, bool> ClearDate { get; init; }
    public required Func<string, string, bool> SetStatus { get; init; }
    public required Func<string, int, bool> SetRepeat { get; init; }
    public required Func<string, Window, Task> EditReminders { get; init; }
    public required Func<string, Window, Task<bool>> Delete { get; init; }
    /// <summary>Brings the editor forward with the row selected.</summary>
    public required Action<string> GoToRow { get; init; }
    /// <summary>Undo / redo in the table; the name of the step, or null when there was none.</summary>
    public required Func<string?> Undo { get; init; }
    public required Func<string?> Redo { get; init; }
    /// <summary>Rows with text that are not on the calendar (no due date, or one that can't be read).</summary>
    public required Func<List<UnplacedRow>> Unplaced { get; init; }
    public required Func<int> UnplacedCount { get; init; }
    /// <summary>Puts rows on days (one undo step); returns how many changed.</summary>
    public required Func<IReadOnlyList<(string RowId, DateTime Date, TimeSpan? Time)>, int> Place { get; init; }
    /// <summary>The table has no due date column: add it back (one undo step).</summary>
    public required Func<bool> AddDueColumn { get; init; }
    public required Func<CalendarPrefs> Prefs { get; init; }
    public required Action<CalendarPrefs> SetPrefs { get; init; }
    /// <summary>The table is on the wallpaper now.</summary>
    public Func<bool> OnWallpaper { get; init; } = () => false;
    public Func<double> Zoom { get; init; } = () => 1;
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

/// <summary>
/// 2.7.0: the calendar. A month of six weeks; every day shows the rows due that day (time and title, coloured like the
/// table: overdue, today, done) and later rounds of repeating tasks (🔁). Hover for the details; click a day (or Enter)
/// to open its pop-up table (<see cref="CalendarDayDialog"/>), which edits the same rows as the table.
/// ◀ ▶ / PageUp PageDown change the month, the arrows the day, Home goes to today, Ctrl+Z / Ctrl+Y undo in the table.
/// The footer keeps the calendar's settings, among them the calendar on the wallpaper (bottom right).
/// </summary>
public sealed class CalendarWindow : Window
{
    // First: the brushes below are made through it (static fields start in the order they are written).
    private static readonly Dictionary<string, IBrush> Paints = new(StringComparer.OrdinalIgnoreCase);

    internal static IBrush Paint(string hex)
    {
        if (!Paints.TryGetValue(hex, out var b)) Paints[hex] = b = new SolidColorBrush(Color.Parse(hex));
        return b;
    }

    internal static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    internal static readonly IBrush TextFg = Paint("#3B3222");
    internal static readonly IBrush SoftFg = Paint("#7A6A48");
    internal static readonly IBrush SundayFg = Paint("#B03A2E");
    internal static readonly IBrush CellBg = Paint("#FFFDF6");
    internal static readonly IBrush OtherMonthBg = Paint("#FBF3D2");
    internal static readonly IBrush TodayCellBg = Paint("#FFF1BF");
    internal static readonly IBrush GridLine = Paint("#E4D08A");
    internal static readonly IBrush TodayEdge = Paint("#C08A1E");
    internal static readonly IBrush SelectedEdge = Paint("#C47B2B");
    internal static readonly IBrush TodayPill = Paint("#E7B84A");
    internal static readonly IBrush ChipBg = Paint("#F4E7B4");
    internal static readonly IBrush ChipOverdue = Paint("#FFD0C8");
    internal static readonly IBrush ChipToday = Paint("#FFE9A8");
    internal static readonly IBrush ChipDone = Paint("#EFEADF");
    internal static readonly IBrush DoneFg = Paint("#9A9080");
    internal static readonly IBrush OverdueFg = Paint("#8A2E22");
    internal static readonly IBrush GhostEdge = Paint("#D4B86A");
    internal static readonly IBrush ToolBg = Paint("#F0D78A");
    internal static readonly IBrush ToolBorder = Paint("#D4B86A");
    private const double DayNumberHeight = 24;
    private const double ChipHeight = 19;

    private readonly CalendarModel _model;
    private DateTime _month;
    private DateTime _selected;
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Foreground = SundayFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Button _addDue;
    private readonly WrapPanel _noticeBox = new() { IsVisible = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Button _place;
    private readonly CalendarDatePicker _jump = new()
    {
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "dd/MM/yyyy",
        Width = 132,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _days = new();
    private readonly Dictionary<DateTime, Border> _cells = new();
    private readonly CheckBox _hideDone = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _weekStart = new() { MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _wallpaper = new() { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _weekStartLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) };
    private readonly TextBlock _wallpaperLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) };
    private readonly Button _prev, _next, _today;
    private CalendarDayDialog? _day;
    private int _chipRows = 3;
    private bool _syncing;
    /// <summary>What the month shows now; a change of the table that changes none of it draws nothing.</summary>
    private string _shown = "";

    public CalendarWindow(CalendarModel model, DateTime? show = null)
    {
        _model = model;
        var zoom = Math.Clamp(model.Zoom(), 0.5, 3);
        _selected = (show ?? model.Now()).Date;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Width = 940 * zoom;
        Height = 700 * zoom;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Paint("#FFF6C2");
        FontFamily = AppFonts.Thai;
        Foreground = TextFg;
        ShowInTaskbar = true;

        _prev = Tool("◀", () => ShowMonth(_month.AddMonths(-1)));
        _next = Tool("▶", () => ShowMonth(_month.AddMonths(1)));
        _today = Tool("", GoToday);
        _prev.Name = "CalendarPrev";
        _next.Name = "CalendarNext";
        _today.Name = "CalendarToday";
        _place = Tool("", () => _ = OpenPlaceAsync(_selected));
        _place.Name = "CalendarPlace";
        _place.Margin = new Thickness(10, 0, 0, 0);
        _addDue = Tool("", () => { if (_model.AddDueColumn()) Render(); });
        _addDue.Name = "CalendarAddDue";
        _noticeBox.Children.Add(_notice);
        _noticeBox.Children.Add(_addDue);
        _jump.Name = "CalendarJump";
        _jump.SelectedDateChanged += (_, _) =>
        {
            if (_syncing || _jump.SelectedDate is not { } d) return;
            _syncing = true;
            _jump.SelectedDate = null; // the box shows "Go to…" again
            _syncing = false;
            ShowDate(d.Date);
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(_next, 1);
        Grid.SetColumn(_today, 2);
        Grid.SetColumn(_jump, 3);
        _title.Margin = new Thickness(10, 0, 10, 0);
        _title.Name = "CalendarTitle";
        Grid.SetColumn(_title, 4);
        Grid.SetColumn(_summary, 5);
        Grid.SetColumn(_place, 6);
        header.Children.Add(_prev);
        header.Children.Add(_next);
        header.Children.Add(_today);
        header.Children.Add(_jump);
        header.Children.Add(_title);
        header.Children.Add(_summary);
        header.Children.Add(_place);

        _hideDone.IsCheckedChanged += (_, _) => { if (!_syncing) SavePrefs(); };
        _weekStart.SelectionChanged += (_, _) => { if (!_syncing && _weekStart.SelectedIndex >= 0) SavePrefs(); };
        _wallpaper.SelectionChanged += (_, _) => { if (!_syncing && _wallpaper.SelectedIndex >= 0) SavePrefs(); };
        _wallpaper.Name = "CalendarWallpaper";
        var options = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _hideDone, _weekStartLabel, _weekStart, _wallpaperLabel, _wallpaper } };
        var footer = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0), Children = { options, _hint } };

        _days.Name = "CalendarDays";
        _days.SizeChanged += (_, _) => FitChips();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_noticeBox, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 12), Children = { header, _noticeBox, footer, _days } };
        Content = Math.Abs(zoom - 1) < 0.001 ? dock : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = dock };

        model.Changed += OnModelChanged;
        Localizer.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            model.Changed -= OnModelChanged;
            Localizer.LanguageChanged -= OnLanguageChanged;
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        ApplyTexts();
        Render();
    }

    /// <summary>The month on show (its first day).</summary>
    public DateTime Month => _month;
    public DateTime Selected => _selected;
    /// <summary>The day pop-up while it is open.</summary>
    public CalendarDayDialog? Day => _day;
    /// <summary>"Put existing rows on the calendar" while it is open (from the calendar or a row's menu).</summary>
    internal CalendarPlaceDialog? Placing { get; private set; }
    internal Button PlaceButton => _place;
    internal string TitleText => _title.Text ?? "";
    internal Border? CellFor(DateTime date) => _cells.TryGetValue(date.Date, out var b) ? b : null;

    /// <summary>Goes to the day's month and selects it; <paramref name="open"/> opens its pop-up with the row selected.</summary>
    public void ShowDate(DateTime date, string? rowId = null, bool open = false)
    {
        _selected = date.Date;
        if (_day != null)
        {
            if (open) _day.ShowDay(date.Date, rowId);
            ShowMonth(new DateTime(date.Year, date.Month, 1));
            return;
        }
        ShowMonth(new DateTime(date.Year, date.Month, 1));
        if (open) OpenDay(date.Date, rowId);
    }

    public void ShowMonth(DateTime month)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        if (_selected.Year != _month.Year || _selected.Month != _month.Month)
            _selected = new DateTime(_month.Year, _month.Month, Math.Min(_selected.Day, DateTime.DaysInMonth(_month.Year, _month.Month)));
        Render();
    }

    private void GoToday() => ShowDate(_model.Now().Date);

    private void OnModelChanged()
    {
        Render(onlyIfChanged: true); // the minute clock and typing elsewhere: no redraw (and no tool tip closed) for nothing
        _day?.Refresh();
    }

    private void OnLanguageChanged()
    {
        ApplyTexts();
        Render();
        _day?.Refresh(force: true);
    }

    private void ApplyTexts()
    {
        Title = Localizer.T("calendar.window_title");
        ToolTip.SetTip(_prev, Localizer.T("calendar.prev_tip"));
        ToolTip.SetTip(_next, Localizer.T("calendar.next_tip"));
        _today.Content = Localizer.T("calendar.today");
        ToolTip.SetTip(_today, Localizer.T("calendar.today_tip"));
        _jump.PlaceholderText = Localizer.T("calendar.jump");
        ToolTip.SetTip(_jump, Localizer.T("calendar.jump_tip"));
        ToolTip.SetTip(_place, Localizer.T("calendar.place_button_tip"));
        _addDue.Content = Localizer.T("calendar.add_due_column");
        _hideDone.Content = Localizer.T("calendar.hide_done");
        _weekStartLabel.Text = Localizer.T("calendar.week_start");
        _wallpaperLabel.Text = Localizer.T("calendar.on_wallpaper");
        ToolTip.SetTip(_wallpaper, Localizer.T("calendar.on_wallpaper_tip"));
        ToolTip.SetTip(_wallpaperLabel, Localizer.T("calendar.on_wallpaper_tip"));
        _syncing = true;
        try
        {
            _weekStart.ItemsSource = new[] { CalendarOps.WeekdayName(DayOfWeek.Sunday), CalendarOps.WeekdayName(DayOfWeek.Monday) };
            _wallpaper.ItemsSource = new[] { Localizer.T("calendar.wall_off"), Localizer.T("calendar.wall_corner"), Localizer.T("calendar.wall_only") };
            SyncPrefs();
        }
        finally { _syncing = false; }
    }

    private void SyncPrefs()
    {
        var p = _model.Prefs();
        _hideDone.IsChecked = p.HideDone;
        _weekStart.SelectedIndex = CalendarOps.NormalizeWeekStart(p.WeekStart);
        _wallpaper.SelectedIndex = (int)p.Wallpaper;
    }

    private void SavePrefs()
    {
        var p = new CalendarPrefs((CalendarWallpaper)Math.Clamp(_wallpaper.SelectedIndex, 0, 2), Math.Max(0, _weekStart.SelectedIndex), _hideDone.IsChecked == true);
        _model.SetPrefs(p);
        Render();
        _day?.Refresh(force: true);
    }

    /// <summary>Draws the month again from the table (cheap: 42 cells); with <paramref name="onlyIfChanged"/> only when
    /// something it shows changed.</summary>
    public void Render(bool onlyIfChanged = false)
    {
        var now = _model.Now();
        var prefs = _model.Prefs();
        var canShowNow = _model.CanShow();
        var range = CalendarOps.MonthGrid(_month.Year, _month.Month, prefs.WeekStart);
        var found = canShowNow ? _model.Items(range[0], range[^1].AddDays(1), prefs.HideDone) : [];
        var unplaced = canShowNow ? _model.UnplacedCount() : 0;
        var key = ShownKey(now, prefs, canShowNow, found) + "|" + unplaced;
        if (onlyIfChanged && key == _shown) return;
        _shown = key;
        _syncing = true;
        try { SyncPrefs(); }
        finally { _syncing = false; }
        _title.Text = CalendarOps.MonthTitle(_month.Year, _month.Month);
        _today.IsEnabled = !(_month.Year == now.Year && _month.Month == now.Month && _selected == now.Date);
        var canShow = canShowNow;
        _notice.Text = canShow ? "" : CalendarOps.NoDueColumnText();
        _noticeBox.IsVisible = !canShow;
        _place.Content = Localizer.T("calendar.place_button", unplaced);
        _place.IsVisible = canShow;
        _place.IsEnabled = unplaced > 0;
        _hint.Text = Localizer.T(prefs.Wallpaper == CalendarWallpaper.Off ? "calendar.hint" : _model.OnWallpaper() ? "calendar.hint_wall_on" : "calendar.hint_wall_next");

        var grid = range;
        var items = found;
        var byDay = CalendarOps.ByDay(items);
        var inMonth = items.Where(i => i.Date.Year == _month.Year && i.Date.Month == _month.Month).ToList();
        _summary.Text = CalendarOps.Summary(inMonth);

        _days.Children.Clear();
        _days.ColumnDefinitions.Clear();
        _days.RowDefinitions.Clear();
        _cells.Clear();
        for (var c = 0; c < 7; c++) _days.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _days.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var r = 0; r < 6; r++) _days.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        var week = CalendarOps.WeekDays(prefs.WeekStart);
        for (var c = 0; c < 7; c++)
        {
            var head = new TextBlock
            {
                Text = CalendarOps.WeekdayShort(week[c]),
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                Foreground = week[c] == DayOfWeek.Sunday ? SundayFg : SoftFg,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            ToolTip.SetTip(head, CalendarOps.WeekdayName(week[c]));
            Grid.SetColumn(head, c);
            _days.Children.Add(head);
        }
        for (var i = 0; i < grid.Count; i++)
        {
            var date = grid[i];
            byDay.TryGetValue(date, out var list);
            var cell = DayCell(date, list ?? [], now, canShow);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, i / 7 + 1);
            _days.Children.Add(cell);
            _cells[date] = cell;
        }
    }

    private string ShownKey(DateTime now, CalendarPrefs prefs, bool canShow, List<CalendarItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_month.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(_selected.Ticks).Append('|')
          .Append(now.Date.Ticks).Append('|').Append(prefs).Append('|').Append(_chipRows).Append('|').Append(Localizer.Current).Append('|')
          .Append(canShow).Append('|').Append(_model.OnWallpaper());
        foreach (var i in items)
            sb.Append('\n').Append(i.RowId).Append(i.Row).Append(i.Due.Ticks).Append(i.HasTime).Append(i.Title).Append(i.Detail).Append(i.Status)
              .Append(i.Done).Append(i.Overdue).Append(i.Today).Append(i.Repeat).Append(string.Join(",", i.Advance)).Append(i.Color).Append(i.Ghost);
        return sb.ToString();
    }

    private Border DayCell(DateTime date, List<CalendarItem> items, DateTime now, bool canShow)
    {
        var inMonth = date.Month == _month.Month && date.Year == _month.Year;
        var today = date == now.Date;
        var selected = date == _selected;
        var number = new TextBlock
        {
            Text = date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 13,
            FontWeight = today ? FontWeight.Bold : FontWeight.SemiBold,
            Foreground = date.DayOfWeek == DayOfWeek.Sunday && !today ? SundayFg : TextFg,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var pill = new Border
        {
            Child = number,
            MinWidth = 24,
            Height = 22,
            Padding = new Thickness(5, 0),
            CornerRadius = new CornerRadius(11),
            Background = today ? TodayPill : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var top = new DockPanel { Height = DayNumberHeight, LastChildFill = false };
        top.Children.Add(pill);
        var real = items.Count(i => !i.Ghost);
        if (real > 0)
        {
            var count = new TextBlock
            {
                Text = real.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                Foreground = SoftFg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            };
            DockPanel.SetDock(count, Dock.Right);
            top.Children.Add(count);
        }
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(top);
        var shown = items.Count <= _chipRows ? items.Count : Math.Max(0, _chipRows - 1);
        foreach (var item in items.Take(shown)) stack.Children.Add(Chip(item));
        if (items.Count > shown)
            stack.Children.Add(new TextBlock
            {
                Text = Localizer.T("calendar.more", items.Count - shown),
                FontSize = 11,
                Foreground = SoftFg,
                Margin = new Thickness(4, 0, 0, 0)
            });
        var cell = new Border
        {
            Background = today ? TodayCellBg : inMonth ? CellBg : OtherMonthBg,
            BorderBrush = selected ? SelectedEdge : today ? TodayEdge : GridLine,
            BorderThickness = new Thickness(selected || today ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2),
            Padding = new Thickness(4, 2, 4, 3),
            ClipToBounds = true,
            Cursor = canShow ? HandCursor : Cursor.Default,
            Opacity = inMonth ? 1 : 0.6,
            Child = stack,
            Tag = date
        };
        var tip = CalendarOps.DayTitle(date) + "\n" + CalendarOps.Summary(items);
        foreach (var i in items.Take(12)) tip += "\n" + CalendarOps.TimeText(i) + "  " + (i.Ghost ? "🔁 " : "") + CalendarOps.TitleText(i);
        if (items.Count > 12) tip += "\n" + Localizer.T("calendar.more", items.Count - 12);
        if (canShow) tip += "\n\n" + Localizer.T("calendar.cell_tip");
        ToolTip.SetTip(cell, tip);
        cell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Select(date);
            if (canShow) Dispatcher.UIThread.Post(() => OpenDay(date));
        };
        return cell;
    }

    private Border Chip(CalendarItem item)
    {
        var text = new TextBlock
        {
            Text = (item.Ghost ? "🔁 " : "") + (item.HasTime ? DueDates.FormatTime(item.Due) + " " : "") + CalendarOps.TitleText(item),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Foreground = item.Done ? DoneFg : item.Overdue ? OverdueFg : item.Ghost ? SoftFg : TextFg,
            FontStyle = item.Ghost ? FontStyle.Italic : FontStyle.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;
        var chip = new Border
        {
            Child = text,
            Height = ChipHeight - 2,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0),
            Background = item.Ghost ? Brushes.Transparent
                : item.Color is { } hex && !item.Done ? Paint(hex)
                : item.Done ? ChipDone : item.Overdue ? ChipOverdue : item.Today ? ChipToday : ChipBg,
            BorderBrush = item.Ghost ? GhostEdge : null,
            BorderThickness = new Thickness(item.Ghost ? 1 : 0),
            Opacity = item.Ghost ? 0.8 : 1,
            Tag = item.RowId
        };
        ToolTip.SetTip(chip, CalendarOps.Describe(item));
        return chip;
    }

    /// <summary>As many item lines as fit in a cell (the rest is "+ N more").</summary>
    private void FitChips()
    {
        var height = _days.Bounds.Height;
        if (height <= 0) return;
        var cell = (height - 24) / 6 - 4 - 5; // header row, margins and padding
        var rows = Math.Clamp((int)Math.Floor((cell - DayNumberHeight) / ChipHeight), 1, 12);
        if (rows == _chipRows) return;
        _chipRows = rows;
        Dispatcher.UIThread.Post(() => Render());
    }

    private void Select(DateTime date)
    {
        var old = _selected;
        _selected = date.Date;
        if (_selected.Year != _month.Year || _selected.Month != _month.Month)
        {
            _month = new DateTime(_selected.Year, _selected.Month, 1);
            Render();
            return;
        }
        if (old != _selected) Render();
    }

    /// <summary>Opens the day's pop-up table (one at a time) and draws the month again when it closes.</summary>
    public void OpenDay(DateTime date, string? rowId = null)
    {
        if (!_model.CanShow()) return;
        if (_day != null) { _day.ShowDay(date, rowId); _day.Activate(); return; }
        _selected = date.Date;
        var day = new CalendarDayDialog(_model, date.Date, rowId);
        _day = day;
        day.DayChanged += d =>
        {
            _selected = d;
            if (d.Year != _month.Year || d.Month != _month.Month) _month = new DateTime(d.Year, d.Month, 1);
            Render();
        };
        day.Closed += (_, _) =>
        {
            if (_day == day) _day = null;
            Render();
            Activate();
        };
        Render();
        _ = day.ShowDialog(this);
    }

    /// <summary>Lists the rows with text that are not on the calendar and puts the ticked ones on a day
    /// (<see cref="CalendarPlaceDialog"/>); then shows the earliest of those days.</summary>
    /// <param name="check">Rows ticked at first (a row's menu → Put on the calendar…); null: rows with a date in them.</param>
    public async Task OpenPlaceAsync(DateTime date, IReadOnlyCollection<string>? check = null)
    {
        if (!_model.CanShow() || Placing != null) return;
        var dialog = new CalendarPlaceDialog(_model, date, check);
        Placing = dialog;
        try { await dialog.ShowDialog(_day ?? (Window)this); }
        finally { Placing = null; }
        if (dialog.Placed > 0 && dialog.FirstDate is { } first)
        {
            _hint.Text = Localizer.T(dialog.Placed == 1 ? "calendar.placed_one" : "calendar.placed", dialog.Placed);
            if (_day == null) ShowDate(first);
            else _day.Refresh(force: true);
        }
        Render();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_day != null || Placing != null) return;
        var focus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var inControl = focus is ComboBox or TextBox or CheckBox || (focus is Visual v && v.FindAncestorOfType<ComboBox>() != null);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            Undone(_model.Undo(), "ui.undo_done");
            return;
        }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))))
        {
            e.Handled = true;
            Undone(_model.Redo(), "ui.redo_done");
            return;
        }
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (inControl && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Space) return;
        switch (e.Key)
        {
            case Key.Left: Select(_selected.AddDays(-1)); break;
            case Key.Right: Select(_selected.AddDays(1)); break;
            case Key.Up: Select(_selected.AddDays(-7)); break;
            case Key.Down: Select(_selected.AddDays(7)); break;
            case Key.PageUp: ShowMonth(_month.AddMonths(-1)); break;
            case Key.PageDown: ShowMonth(_month.AddMonths(1)); break;
            case Key.Home: GoToday(); break;
            case Key.Enter or Key.Space: OpenDay(_selected); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Undone(string? label, string key)
    {
        if (label == null) return;
        _hint.Text = Localizer.T(key, label);
    }

    internal static Button Tool(string text, Action click)
    {
        var b = new Button
        {
            Content = text,
            Background = ToolBg,
            Foreground = TextFg,
            BorderBrush = ToolBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(8),
            FontFamily = AppFonts.Thai,
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += (_, _) => click();
        return b;
    }

}
