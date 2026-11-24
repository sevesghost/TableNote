using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// 2.7.0: the calendar card on the wallpaper (bottom right): today in large, this month with today marked and a dot
/// under every day that has rows due, and below it what is overdue and what comes next (time and title). Built from
/// the same rows as the calendar window (<see cref="CalendarOps"/>), so the wallpaper follows the table.
/// </summary>
public static class CalendarImage
{
    public const double CellWidth = 41;
    public const double CellHeight = 31;
    public const double Padding = 12;
    public const int MaxUpcoming = 6;
    public const string TodayFill = "#E7B84A";
    public const string TodayNumber = "#C08A1E";
    public const string OverdueDot = "#D9534F";
    public const string TodayDot = "#E0962A";
    public const string NextDot = "#8A7A55";
    public const string DoneDot = "#B8AE98";
    public const string SoftText = "#7A6A48";
    public const string SundayText = "#B03A2E";

    // First: the brushes are made through it (static fields start in the order they are written).
    private static readonly Dictionary<string, IBrush> Paints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The card in DIPs; its size is what it needs (about 310 × 400): today in large, the month, what comes next.</summary>
    public static Border BuildCard(NoteDocument doc, DateTime now, CalendarPrefs prefs, IBrush background)
    {
        var grid = CalendarOps.MonthGrid(now.Year, now.Month, prefs.WeekStart);
        var items = CalendarOps.Items(doc, grid[0], grid[^1].AddDays(1), now, prefs.HideDone);
        var byDay = CalendarOps.ByDay(items);

        var stack = new StackPanel { Spacing = 4 };
        // Today, large: "24 | Tuesday / November 2026".
        var big = new TextBlock
        {
            Text = now.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontFamily = AppFonts.Thai,
            FontSize = 38,
            FontWeight = FontWeight.Bold,
            Foreground = Brush(TodayNumber),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0)
        };
        var names = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.T("calendar.weekday_title", CalendarOps.WeekdayName(now.DayOfWeek)),
                    FontFamily = AppFonts.Thai,
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush(ViewImage.Text)
                },
                new TextBlock
                {
                    Text = CalendarOps.MonthTitle(now.Year, now.Month),
                    FontFamily = AppFonts.Thai,
                    FontSize = 13,
                    Foreground = Brush(SoftText),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(names, 1);
        head.Children.Add(big);
        head.Children.Add(names);
        stack.Children.Add(head);

        var days = new Grid();
        for (var c = 0; c < 7; c++) days.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(CellWidth)));
        days.RowDefinitions.Add(new RowDefinition(new GridLength(20)));
        for (var r = 0; r < 6; r++) days.RowDefinitions.Add(new RowDefinition(new GridLength(CellHeight)));
        var week = CalendarOps.WeekDays(prefs.WeekStart);
        for (var c = 0; c < 7; c++)
        {
            var dayName = new TextBlock
            {
                Text = CalendarOps.WeekdayShort(week[c]),
                FontFamily = AppFonts.Thai,
                FontSize = 12,
                Foreground = Brush(week[c] == DayOfWeek.Sunday ? SundayText : SoftText),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dayName, c);
            days.Children.Add(dayName);
        }
        for (var i = 0; i < grid.Count; i++)
        {
            var date = grid[i];
            byDay.TryGetValue(date, out var list);
            var cell = DayCell(date, now, list, week[i % 7]);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, i / 7 + 1);
            days.Children.Add(cell);
        }
        stack.Children.Add(days);

        var upcoming = CalendarOps.Upcoming(doc, now, MaxUpcoming, out var more);
        stack.Children.Add(new Border { Height = 1, Background = Brush(ViewImage.GridLine), Margin = new Thickness(0, 4, 0, 2) });
        if (upcoming.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Localizer.T("calendar.wall_nothing"),
                FontFamily = AppFonts.Thai,
                FontSize = 13,
                Foreground = Brush(SoftText),
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            var lines = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            for (var i = 0; i < upcoming.Count; i++)
            {
                var item = upcoming[i];
                lines.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var colour = Brush(item.Overdue ? SundayText : ViewImage.Text);
                var weight = item.Today && !item.Overdue ? FontWeight.SemiBold : FontWeight.Normal;
                var when = item.Overdue ? Localizer.T("calendar.overdue") : CalendarOps.When(item.Date, now);
                if (item.Overdue && item.Date != now.Date) when += " " + CalendarOps.ShortDate(item.Date);
                AddLine(lines, i, 0, when, colour, weight, trim: false);
                AddLine(lines, i, 1, CalendarOps.TimeText(item), colour, weight, trim: false);
                AddLine(lines, i, 2, CalendarOps.TitleText(item), colour, weight, trim: true);
            }
            stack.Children.Add(lines);
            if (more > 0)
                stack.Children.Add(new TextBlock
                {
                    Text = Localizer.T("calendar.wall_more", more),
                    FontFamily = AppFonts.Thai,
                    FontSize = 12,
                    Foreground = Brush(SoftText)
                });
        }

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(WallpaperImage.CardCorner),
            BoxShadow = BoxShadows.Parse("0 4 18 0 #59000000"),
            Width = CellWidth * 7 + Padding * 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(Padding),
            Child = stack
        };
    }

    private static Control DayCell(DateTime date, DateTime now, List<CalendarItem>? items, DayOfWeek weekday)
    {
        var inMonth = date.Month == now.Month && date.Year == now.Year;
        var today = date == now.Date;
        var number = new TextBlock
        {
            Text = date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontFamily = AppFonts.Thai,
            FontSize = 14,
            FontWeight = today ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Brush(today ? ViewImage.Text : weekday == DayOfWeek.Sunday ? SundayText : ViewImage.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var face = new Border
        {
            Width = 27,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = today ? Brush(TodayFill) : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = number
        };
        var panel = new Grid { Opacity = inMonth ? 1 : 0.38 };
        panel.Children.Add(face);
        if (items is { Count: > 0 })
        {
            var colour = items.Any(i => i.Overdue) ? OverdueDot
                : items.Any(i => i.Today && !i.Done) ? TodayDot
                : items.All(i => i.Done) ? DoneDot
                : NextDot;
            panel.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = Brush(colour),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 1)
            });
        }
        return panel;
    }

    private static void AddLine(Grid lines, int row, int column, string text, IBrush colour, FontWeight weight, bool trim)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = AppFonts.Thai,
            FontSize = 13,
            FontWeight = weight,
            Foreground = colour,
            Margin = new Thickness(0, 1, column < 2 ? 8 : 0, 1),
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            MaxLines = 1
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        lines.Children.Add(block);
    }

    /// <summary>Immutable brushes, shared by every card.</summary>
    internal static IBrush Brush(string hex)
    {
        lock (Paints)
        {
            if (!Paints.TryGetValue(hex, out var b)) Paints[hex] = b = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(hex));
            return b;
        }
    }
}
