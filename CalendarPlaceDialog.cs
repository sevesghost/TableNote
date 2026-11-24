using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// 2.7.0: puts rows written before the calendar (or without a date) on it. Rows that have a due date are on the
/// calendar already; this lists the rows with text whose due date is empty or can't be read, with any date found in
/// the row (another column, or inside its text). Tick rows, then: each on the date found in it, or on the chosen day,
/// at the chosen time (empty = all day). One undo step in the table.
/// </summary>
public sealed class CalendarPlaceDialog : Window
{
    private static readonly IBrush FoundFg = CalendarWindow.Paint("#3F7A47");
    private static readonly IBrush RowBg = CalendarWindow.Paint("#FFFDF6");

    private readonly CalendarModel _model;
    private readonly List<(UnplacedRow Row, CheckBox Box)> _rows = [];
    private readonly CalendarDatePicker _date = new()
    {
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "dd/MM/yyyy", // CE, day/month like the rest of the table
        Width = 150,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBox _time = new() { Width = 80, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly CheckBox _useFound = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _go;
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = CalendarWindow.SundayFg };
    private bool _timeTyped;
    private bool _syncing;

    /// <summary>How many rows were put on the calendar (0 when closed without).</summary>
    public int Placed { get; private set; }
    /// <summary>The earliest day a row was put on.</summary>
    public DateTime? FirstDate { get; private set; }
    internal IReadOnlyList<(UnplacedRow Row, CheckBox Box)> Rows => _rows;

    /// <param name="date">The day to put the rows on (the day selected in the calendar).</param>
    /// <param name="check">Rows ticked at first; null: the rows that have a date written in them.</param>
    public CalendarPlaceDialog(CalendarModel model, DateTime date, IReadOnlyCollection<string>? check = null)
    {
        _model = model;
        var zoom = Math.Clamp(model.Zoom(), 0.5, 3);
        Title = Localizer.T("calendar.place_window");
        Width = 760 * zoom;
        Height = 600 * zoom;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = CalendarWindow.Paint("#FFF6C2");
        FontFamily = AppFonts.Thai;
        Foreground = CalendarWindow.TextFg;

        var rows = model.Unplaced();
        var anyFound = rows.Any(r => r.Found != null);
        var heading = new TextBlock { Text = Localizer.T("calendar.place_title"), FontSize = 18, FontWeight = FontWeight.Bold };
        var intro = new TextBlock { Text = Localizer.T("calendar.place_intro"), TextWrapping = TextWrapping.Wrap, Foreground = CalendarWindow.SoftFg };
        var all = CalendarWindow.Tool(Localizer.T("calendar.place_all"), () => Tick(_ => true));
        var none = CalendarWindow.Tool(Localizer.T("calendar.place_clear"), () => Tick(_ => false));
        var found = CalendarWindow.Tool(Localizer.T("calendar.place_found_only"), () => Tick(r => r.Found != null));
        all.Name = "PlaceAll";
        found.Name = "PlaceFound";
        found.IsVisible = anyFound;
        var picks = new WrapPanel { Margin = new Thickness(0, 6, 0, 4), Children = { all, none, found } };

        var list = new StackPanel { Spacing = 2 };
        foreach (var r in rows)
        {
            var box = new CheckBox
            {
                IsChecked = check != null ? check.Contains(r.RowId) : r.Found != null,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = r.RowId
            };
            box.IsCheckedChanged += (_, _) => { if (!_syncing) Count(); };
            var note = r.Found is { } f
                ? Localizer.T("calendar.place_found", r.FoundIn ?? "", r.FoundTime ? DaysCounter.DateTimeText(f) : DaysCounter.Date(f)) // dd/MM/yyyy as in the table
                : r.Due.Length > 0 ? Localizer.T("calendar.place_unreadable", TableLimits.Clip(r.Due, 40)) : Localizer.T("calendar.place_no_date");
            if (r.Done) note += " · " + Localizer.T("calendar.place_done_row");
            var number = new TextBlock { Text = (r.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), MinWidth = 34, VerticalAlignment = VerticalAlignment.Center, Foreground = CalendarWindow.SoftFg };
            var title = new TextBlock
            {
                Text = r.Title.Length > 0 ? r.Title : Localizer.T("calendar.untitled", r.Row + 1),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = r.Done ? CalendarWindow.DoneFg : CalendarWindow.TextFg
            };
            var hint = new TextBlock
            {
                Text = note,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = r.Found != null ? FoundFg : CalendarWindow.SoftFg,
                Margin = new Thickness(10, 0, 0, 0)
            };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Background = RowBg, Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(number, 1);
            Grid.SetColumn(title, 2);
            Grid.SetColumn(hint, 3);
            line.Children.Add(box);
            line.Children.Add(number);
            line.Children.Add(title);
            line.Children.Add(hint);
            // A click anywhere on the line ticks it, not only on the small box.
            line.PointerPressed += (_, e) =>
            {
                if (e.Source is CheckBox || !e.GetCurrentPoint(line).Properties.IsLeftButtonPressed) return;
                box.IsChecked = box.IsChecked != true;
                e.Handled = true;
            };
            ToolTip.SetTip(line, Localizer.T("calendar.tip_row", r.Row + 1));
            list.Children.Add(new Border { Child = line, BorderBrush = CalendarWindow.GridLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3) });
            _rows.Add((r, box));
        }
        Control body = rows.Count == 0
            ? new TextBlock { Text = Localizer.T("calendar.place_none"), Margin = new Thickness(4, 12), Foreground = CalendarWindow.SoftFg, TextWrapping = TextWrapping.Wrap }
            : new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _useFound.Content = Localizer.T("calendar.place_use_found");
        _useFound.IsChecked = anyFound;
        _useFound.IsVisible = anyFound;
        _date.SelectedDate = date.Date;
        _date.PlaceholderText = Localizer.T("date.placeholder");
        _date.Name = "PlaceDate";
        _date.SelectedDateChanged += (_, _) =>
        {
            // The time follows the day (09:00, or the next hour today) until one is typed.
            if (!_timeTyped && _date.SelectedDate is { } d) SetTime(CalendarOps.DefaultTimeFor(d.Date, model.Now()));
        };
        SetTime(CalendarOps.DefaultTimeFor(date, model.Now()));
        _time.PlaceholderText = Localizer.T("calendar.all_day");
        _time.Name = "PlaceTime";
        _time.TextChanged += (_, _) => { if (!_syncing) _timeTyped = true; };
        ToolTip.SetTip(_time, Localizer.T("calendar.place_time_tip"));
        var when = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = Localizer.T("calendar.place_date"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                _date,
                new TextBlock { Text = Localizer.T("calendar.place_time"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) },
                _time
            }
        };

        _go = CalendarWindow.Tool("", Go);
        _go.Name = "PlaceGo";
        _go.IsDefault = true;
        var cancel = CalendarWindow.Tool(Localizer.T("common.cancel"), Close);
        cancel.IsCancel = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { _go, cancel } };
        var options = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children = { _useFound, when, _message, buttons } };
        var top = new StackPanel { Spacing = 4, Children = { heading, intro, picks } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(options, Dock.Bottom);
        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 12), Children = { top, options, body } };
        Content = Math.Abs(zoom - 1) < 0.001 ? dock : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = dock };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        Count();
    }

    private void SetTime(TimeSpan time)
    {
        _syncing = true;
        _time.Text = DueDates.FormatTime(DateTime.Today + time);
        _syncing = false;
    }

    private void Tick(Func<UnplacedRow, bool> on)
    {
        _syncing = true;
        foreach (var (row, box) in _rows) box.IsChecked = on(row);
        _syncing = false;
        Count();
    }

    private int Checked => _rows.Count(r => r.Box.IsChecked == true);

    private void Count()
    {
        var n = Checked;
        _go.Content = Localizer.T("calendar.place_go", n);
        _go.IsEnabled = n > 0;
    }

    private void Go()
    {
        if (!CalendarOps.TryParseTime(_time.Text, out var time))
        {
            _message.Text = Localizer.T("date.bad_time", (_time.Text ?? "").Trim());
            return;
        }
        var useFound = _useFound.IsVisible && _useFound.IsChecked == true;
        var chosen = _date.SelectedDate?.Date;
        var list = new List<(string RowId, DateTime Date, TimeSpan? Time)>();
        foreach (var (row, box) in _rows)
        {
            if (box.IsChecked != true) continue;
            if (useFound && row.Found is { } f) list.Add((row.RowId, f.Date, row.FoundTime ? f.TimeOfDay : time));
            else if (chosen is { } d) list.Add((row.RowId, d, time));
            else
            {
                _message.Text = Localizer.T("calendar.place_pick_date");
                return;
            }
        }
        if (list.Count == 0) return;
        Placed = _model.Place(list);
        FirstDate = list.Min(x => x.Date);
        Close();
    }
}
