using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// 2.6.0: pop-up of a days counter cell. Shows how long the row's work has been open (or how long it took),
/// the start date (changeable), the status and when it last changed, and the row history newest first:
/// status updates, edits and progress notes, each with its date, time and day since the start.
/// Everything is written at once (start date as a normal undo step, notes straight to the history).
/// </summary>
public sealed class DaysDialog : Window
{
    public sealed class Model
    {
        /// <summary>Fresh content; null when the row or column is gone (the pop-up closes).</summary>
        public required Func<DaysInfo?> Info { get; init; }
        public required Func<DateTime, bool> SetStart { get; init; }
        public required Func<string, bool> AddNote { get; init; }
        public required Func<RowEvent, bool> RemoveNote { get; init; }
    }

    private static readonly IBrush TextFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush SoftFg = new SolidColorBrush(Color.Parse("#7A6A48"));
    private static readonly IBrush ItemBg = new SolidColorBrush(Color.Parse("#FFFBE8"));
    private static readonly IBrush NoteBar = new SolidColorBrush(Color.Parse("#E0962A"));
    private static readonly IBrush StatusBar = new SolidColorBrush(Color.Parse("#4E9A5B"));
    private static readonly IBrush AddedBar = new SolidColorBrush(Color.Parse("#7F95B5"));
    private static readonly IBrush EditBar = new SolidColorBrush(Color.Parse("#C2B183"));
    private static readonly IBrush OpenFg = new SolidColorBrush(Color.Parse("#9C4A12"));
    private static readonly IBrush DoneFg = new SolidColorBrush(Color.Parse("#3F7A47"));
    private static readonly IBrush DangerFg = new SolidColorBrush(Color.Parse("#B03A2E"));

    private readonly Model _model;
    private readonly TextBlock _headline = new() { FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _range = new() { Foreground = SoftFg, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Foreground = TextFg, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _lastUpdate = new() { Foreground = TextFg, TextWrapping = TextWrapping.Wrap };
    private readonly CalendarDatePicker _start;
    private readonly TextBox _note;
    private readonly StackPanel _timeline = new() { Spacing = 6 };
    private bool _syncing;

    /// <param name="zoom">The table's zoom (A− / A+), so the pop-up text is as large as the table's.</param>
    /// <param name="maxHeight">Screen height left for the pop-up (DIPs); the history scrolls inside.</param>
    public DaysDialog(string heading, Model model, double zoom = 1, double? maxHeight = null)
    {
        _model = model;
        zoom = Math.Clamp(zoom, 0.5, 3);
        Title = Localizer.T("days.title");
        Width = 560 * zoom;
        Height = Math.Max(300, Math.Min(640 * zoom, maxHeight ?? double.MaxValue));
        MinWidth = Math.Min(420 * zoom, Width);
        MinHeight = Math.Min(360 * zoom, Height);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        FontFamily = AppFonts.Thai;
        Foreground = TextFg;

        var title = new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };

        _start = new CalendarDatePicker
        {
            SelectedDateFormat = CalendarDatePickerFormat.Custom,
            CustomDateFormatString = "dd/MM/yyyy", // CE, day/month like the rest of the table
            PlaceholderText = Localizer.T("date.placeholder"),
            FontFamily = AppFonts.Thai,
            Width = 150
        };
        _start.SelectedDateChanged += (_, _) =>
        {
            if (_syncing || _start.SelectedDate is not { } d) return;
            if (_model.SetStart(d.Date)) Render();
        };
        var today = new Button { Content = Localizer.T("days.today_button"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4) };
        today.Click += (_, _) => { if (_model.SetStart(DateTime.Today)) Render(); };
        var startRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                new TextBlock { Text = Localizer.T("days.start"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                _start, today
            }
        };

        _note = new TextBox
        {
            PlaceholderText = Localizer.T("days.note_placeholder"),
            MaxLength = RowHistoryOps.MaxNoteChars,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MinHeight = 34
        };
        var add = new Button { Content = Localizer.T("days.add_note"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Top };
        add.Click += (_, _) => AddNote();
        _note.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Return) return;
            e.Handled = true;
            AddNote();
        }, RoutingStrategies.Tunnel);
        var noteRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 4) };
        Grid.SetColumn(add, 1);
        noteRow.Children.Add(_note);
        noteRow.Children.Add(add);

        var header = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                title, _headline, _range, startRow, _status, _lastUpdate,
                new Border { Height = 1, Background = EditBar, Margin = new Thickness(0, 8, 0, 4) },
                new TextBlock { Text = Localizer.T("days.history"), FontWeight = FontWeight.Bold },
                noteRow
            }
        };

        var close = new Button { Content = Localizer.T("detail.close"), IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        var hint = new TextBlock { Text = Localizer.T("days.history_note"), FontSize = 12, Foreground = SoftFg, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(close, 1);
        footer.Children.Add(hint);
        footer.Children.Add(close);

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var body = new DockPanel
        {
            Margin = new Thickness(16, 14),
            Children = { header, footer, new ScrollViewer { Content = _timeline, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } }
        };
        Content = Math.Abs(zoom - 1) < 0.001 ? body : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = body };

        Opened += (_, _) => _note.Focus();
        Render();
    }

    private void AddNote()
    {
        var text = _note.Text ?? "";
        if (text.Trim().Length == 0 || !_model.AddNote(text)) return;
        _note.Text = "";
        Render();
    }

    /// <summary>Reads the row again and redraws everything (after each change, so the numbers always match).</summary>
    private void Render()
    {
        var info = _model.Info();
        if (info == null) { Close(); return; }
        _headline.Text = info.Headline;
        _headline.Foreground = info.Count.State switch
        {
            DaysState.Done or DaysState.DoneUnknown => DoneFg,
            DaysState.Counting or DaysState.Future => OpenFg,
            _ => TextFg
        };
        _range.Text = info.Range;
        _status.Text = info.StatusLine ?? "";
        _status.IsVisible = info.StatusLine != null;
        _lastUpdate.Text = info.LastUpdateLine;
        _syncing = true;
        try
        {
            _start.SelectedDate = info.Count.State is DaysState.Blank or DaysState.Invalid ? null : info.Count.Start;
        }
        finally { _syncing = false; }

        _timeline.Children.Clear();
        if (info.Timeline.Count == 0)
        {
            _timeline.Children.Add(new TextBlock { Text = Localizer.T("days.empty_history"), Foreground = SoftFg, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var item in info.Timeline) _timeline.Children.Add(Item(item));
    }

    private Control Item(DaysTimelineItem item)
    {
        var when = new TextBlock
        {
            Text = item.Offset.Length > 0 ? item.When + " · " + item.Offset : item.When,
            FontSize = 12, Foreground = SoftFg, TextWrapping = TextWrapping.Wrap
        };
        var title = new TextBlock
        {
            Text = item.Title, TextWrapping = TextWrapping.Wrap,
            FontWeight = item.Event.Kind is RowEventKinds.Note or RowEventKinds.Status ? FontWeight.SemiBold : FontWeight.Normal
        };
        var text = new StackPanel { Spacing = 1, Children = { when, title } };
        if (!string.IsNullOrEmpty(item.Body))
            text.Children.Add(new TextBlock { Text = item.Body, TextWrapping = TextWrapping.Wrap });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        if (item.IsNote)
        {
            // Two clicks: the first one asks ("Delete?"), so a slip of the mouse never loses a note.
            var delete = new Button
            {
                Content = "✕", Padding = new Thickness(8, 2), Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, BorderThickness = new Thickness(0)
            };
            ToolTip.SetTip(delete, Localizer.T("days.delete_note"));
            var armed = false;
            delete.Click += (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    delete.Content = Localizer.T("days.delete_confirm");
                    delete.Foreground = DangerFg;
                    return;
                }
                if (_model.RemoveNote(item.Event)) Render();
            };
            delete.LostFocus += (_, _) =>
            {
                armed = false;
                delete.Content = "✕";
                delete.ClearValue(ForegroundProperty);
            };
            Grid.SetColumn(delete, 1);
            grid.Children.Add(delete);
        }
        return new Border
        {
            Background = ItemBg,
            BorderBrush = item.Event.Kind switch
            {
                RowEventKinds.Note => NoteBar,
                RowEventKinds.Status => StatusBar,
                RowEventKinds.Added => AddedBar,
                _ => EditBar
            },
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 6),
            Child = grid
        };
    }
}
