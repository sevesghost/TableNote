using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// 2.6.0: the activity bar at the bottom: what TableNote is doing now (a progress bar while it works: export, picture,
/// print, wallpaper), edits waiting for the autosave, the last thing it did with the time, when it last saved, a History
/// list (<see cref="ActivityLog"/>) and ✕ to hide it (View → Activity bar shows it again; kept in settings.json).
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush DotReady = new SolidColorBrush(Color.Parse("#4E9A5B"));
    private static readonly IBrush DotBusy = new SolidColorBrush(Color.Parse("#D9A21B"));
    private static readonly IBrush DotWarning = new SolidColorBrush(Color.Parse("#E07A1F"));
    private static readonly IBrush DotError = new SolidColorBrush(Color.Parse("#C0392B"));
    private static readonly IBrush HistoryTime = new SolidColorBrush(Color.Parse("#8A7A55"));

    private bool _activityRefreshQueued;
    private StackPanel? _historyList;

    private void InitActivityBar()
    {
        ActivityLog.Shared.Changed += OnActivityChanged;
        Closed += (_, _) => ActivityLog.Shared.Changed -= OnActivityChanged;
        ActivityHideButton.Click += (_, _) => SetActivityVisible(false, announce: true);
        ActivityBox.IsChecked = _settings.ShowActivity;
        ActivityBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) SetActivityVisible(ActivityBox.IsChecked == true, announce: false);
        };
        ActivityHistoryButton.Flyout = BuildHistoryFlyout();
        ActivityBar.PointerEntered += (_, _) => ToolTip.SetTip(ActivityBar, ActivityTip());
        ApplyActivityTexts();
        ApplyActivityVisibility();
        RefreshActivityBar();
    }

    private void ApplyActivityTexts()
    {
        ActivityHistoryButton.Content = Localizer.T("activity.history");
        ToolTip.SetTip(ActivityHideButton, Localizer.T("activity.hide"));
        RefreshActivityBar();
    }

    /// <summary>Any thread may report; the bar is redrawn once on the UI thread.</summary>
    private void OnActivityChanged()
    {
        if (_activityRefreshQueued) return;
        _activityRefreshQueued = true;
        Dispatcher.UIThread.Post(() => { _activityRefreshQueued = false; RefreshActivityBar(); });
    }

    private void SetActivityVisible(bool show, bool announce)
    {
        if (_settings.ShowActivity != show)
        {
            _settings.ShowActivity = show;
            _settings.Save(_path);
        }
        if (ActivityBox.IsChecked != show) ActivityBox.IsChecked = show;
        ApplyActivityVisibility();
        RefreshChecks();
        if (announce && !show) StatusText.Text = Localizer.T("activity.hidden");
    }

    private void ApplyActivityVisibility() => ActivityBar.IsVisible = _settings.ShowActivity && _view == ViewMode.Edit;

    /// <summary>Now (running work, waiting edits) or the last thing done; the time of the last save on the right.</summary>
    private void RefreshActivityBar()
    {
        var log = ActivityLog.Shared;
        if (log.Current is { } now)
        {
            ActivityDot.Fill = DotBusy;
            ActivityProgress.IsVisible = true;
            ActivityProgress.IsIndeterminate = now.Progress == null;
            if (now.Progress is { } p) ActivityProgress.Value = p * 100;
            ActivityText.Text = now.Text;
        }
        else
        {
            ActivityProgress.IsVisible = false;
            if (_saveTimer.IsEnabled)
            {
                ActivityDot.Fill = DotBusy;
                ActivityText.Text = Localizer.T("activity.pending");
            }
            else if (log.Last is { } last)
            {
                ActivityDot.Fill = last.Kind switch { ActivityLevel.Error => DotError, ActivityLevel.Warning => DotWarning, _ => DotReady };
                ActivityText.Text = EntryLine(last);
            }
            else
            {
                ActivityDot.Fill = DotReady;
                ActivityText.Text = Localizer.T("activity.ready");
            }
        }
        ActivitySaved.Text = log.LastOf("save") is { } saved ? Localizer.T("activity.last_saved", Clock(saved.Time)) : "";
        if (_historyList != null) FillHistory(_historyList);
    }

    private static string Clock(DateTime t) => t.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Saved note.json · 10:31:05 (5 times)".</summary>
    private static string EntryLine(ActivityEntry e) =>
        e.Text + " · " + Clock(e.Time) + (e.Count > 1 ? " " + Localizer.T("activity.count", e.Count) : "");

    private string ActivityTip()
    {
        var lines = new List<string> { Localizer.T("activity.tip") };
        if (_reminders.LastCheck is { } c) lines.Add(Localizer.T("activity.reminders_checked", Clock(c)));
        return string.Join("\n", lines);
    }

    // ───────────────────────── History ─────────────────────────

    private Flyout BuildHistoryFlyout()
    {
        var list = new StackPanel { Spacing = 3 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
        var note = new TextBlock { FontSize = 11, Foreground = HistoryTime, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        var open = new Button { Classes = { "tool" }, FontSize = 12, Padding = new Thickness(8, 1), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => OpenDataFolder();
        var panel = new StackPanel
        {
            Width = 440, Spacing = 4, Margin = new Thickness(2),
            Children =
            {
                title,
                new ScrollViewer { MaxHeight = 320, Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#E4D08A")), Margin = new Thickness(0, 4) },
                note, open
            }
        };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Opening += (_, _) =>
        {
            title.Text = Localizer.T("activity.history_title");
            note.Text = Localizer.T("activity.history_note");
            open.Content = Localizer.T("activity.open_folder");
            _historyList = list;
            FillHistory(list);
        };
        flyout.Closed += (_, _) => _historyList = null;
        return flyout;
    }

    /// <summary>Newest first: time, a dot for done / warning / error, what it was, how long it took.</summary>
    private void FillHistory(StackPanel list)
    {
        list.Children.Clear();
        var log = ActivityLog.Shared;
        if (log.Current is { } now)
            list.Children.Add(HistoryRow(DateTime.Now, DotBusy, now.Text, null));
        var entries = log.Entries;
        if (entries.Count == 0 && log.Current == null)
        {
            list.Children.Add(new TextBlock { Text = Localizer.T("activity.history_empty"), FontSize = 12, Foreground = HistoryTime });
            return;
        }
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            var dot = e.Kind switch { ActivityLevel.Error => DotError, ActivityLevel.Warning => DotWarning, _ => DotReady };
            var text = e.Text + (e.Count > 1 ? " " + Localizer.T("activity.count", e.Count) : "");
            list.Children.Add(HistoryRow(e.Time, dot, text, e.Duration));
        }
    }

    private static Control HistoryRow(DateTime time, IBrush dot, string text, TimeSpan? took)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        var t = new TextBlock { Text = Clock(time), FontSize = 12, Foreground = HistoryTime, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        var d = new Ellipse { Width = 7, Height = 7, Fill = dot, Margin = new Thickness(0, 5, 7, 0), VerticalAlignment = VerticalAlignment.Top };
        var x = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var s = new TextBlock
        {
            Text = took is { TotalSeconds: >= 0.3 } k ? Localizer.T("activity.took", k.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) : "",
            FontSize = 11, Foreground = HistoryTime, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(d, 1);
        Grid.SetColumn(x, 2);
        Grid.SetColumn(s, 3);
        grid.Children.Add(t);
        grid.Children.Add(d);
        grid.Children.Add(x);
        grid.Children.Add(s);
        return grid;
    }

    /// <summary>The folder with note.json, settings.json and tablenote.log, in Explorer (Files on Linux).</summary>
    private void OpenDataFolder()
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("ui.action_failed", ex.Message);
            AppLog.Write("ui", "open folder failed: " + ex.Message);
        }
    }

    /// <summary>First entry: the table that was opened and how long the start took (also a note.json that is locked or
    /// could not be read completely).</summary>
    private void NoteStart()
    {
        double seconds;
        try { seconds = Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds); }
        catch { seconds = 0; }
        ActivityLog.Shared.Add(ActivityLevel.Done, Localizer.T("activity.loaded", _session.Doc.Rows.Count,
            seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), "load");
        if (_placeholder) ActivityLog.Shared.Add(ActivityLevel.Warning, Localizer.T("load.locked_waiting"), "load-warning");
        else if (!string.IsNullOrEmpty(_loadWarning)) ActivityLog.Shared.Add(ActivityLevel.Warning, _loadWarning, "load-warning");
    }

    /// <summary>Short pause so the bar shows "working…" before work that keeps the UI thread busy (picture, print).</summary>
    private static Task LetBarDraw() => Task.Delay(40);
}
