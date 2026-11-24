using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CalendarTests")]

namespace TableNote;

/// <summary>
/// 2.7.0: the calendar (<see cref="CalendarWindow"/>, toolbar Calendar / F9 / tray / a row's menu → Show in calendar)
/// and its day pop-up (<see cref="CalendarDayDialog"/>). The calendar keeps nothing of its own: its items are the
/// table's rows with a due date, so every change made there goes through here into the table, with an undo step,
/// a save and a reminder check, and every change of the table redraws the open calendar (<see cref="NotifyCalendar"/>).
/// The calendar can also be drawn on the wallpaper at the bottom right (MainWindow.Wallpaper.cs, settings.json).
/// </summary>
public partial class MainWindow
{
    private CalendarWindow? _calendar;
    private CalendarModel? _calendarModel;
    private DispatcherTimer? _calendarTimer;
    private MenuItem _calendarItem = null!; // Table → Calendar (F9)
    private MenuItem _calendarWallMenu = null!; // View → Calendar on the wallpaper

    /// <summary>The open calendar window, for tests.</summary>
    internal CalendarWindow? Calendar => _calendar;

    private void InitCalendar()
    {
        CalendarButton.Click += (_, _) => OpenCalendar();
        var prevRow = _extraRowMenuItems;
        _extraRowMenuItems = rowId => Join(prevRow?.Invoke(rowId), CalendarRowMenu(rowId));
    }

    private void ApplyCalendarTexts()
    {
        CalendarButton.Content = Localizer.T("toolbar.calendar");
        ToolTip.SetTip(CalendarButton, Localizer.T("tip.calendar"));
    }

    /// <summary>Table → Calendar… and View → Calendar on the wallpaper (radio items, filled when the menu opens).</summary>
    private void AddCalendarMenuItems()
    {
        _calendarItem = Act("menu.calendar", () => OpenCalendar());
        _calendarItem.InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F9);
        _tableMenu.Items.Insert(_tableMenu.Items.IndexOf(_addAlertItem), _calendarItem);
        _calendarWallMenu = new MenuItem();
        _calendarWallMenu.Items.Add(new MenuItem()); // filled when it opens
        _calendarWallMenu.SubmenuOpened += (_, _) => FillCalendarWallMenu();
        _viewMenu.Items.Insert(_viewMenu.Items.IndexOf(_inkLayersMenu) + 1, _calendarWallMenu);
        ApplyCalendarMenuTexts();
    }

    private void ApplyCalendarMenuTexts()
    {
        if (_calendarItem == null) return;
        _calendarItem.Header = Localizer.T("menu.calendar");
        ToolTip.SetTip(_calendarItem, Localizer.T("tip.calendar"));
        _calendarWallMenu.Header = Localizer.T("menu.calendar_wallpaper");
        ToolTip.SetTip(_calendarWallMenu, Localizer.T("calendar.on_wallpaper_tip"));
    }

    private void FillCalendarWallMenu()
    {
        _calendarWallMenu.Items.Clear();
        var keys = new[] { "calendar.wall_off", "calendar.wall_corner", "calendar.wall_only" };
        for (var i = 0; i < keys.Length; i++)
        {
            var mode = (CalendarWallpaper)i;
            var item = new MenuItem
            {
                Header = Localizer.T(keys[i]),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.CalendarWallpaper == mode
            };
            item.Click += (_, _) => SetCalendarPrefs(_settings.CalendarPrefs with { Wallpaper = mode });
            _calendarWallMenu.Items.Add(item);
        }
    }

    /// <summary>Opens the calendar (or brings it forward). With a date: that month, the day selected; with
    /// <paramref name="openDay"/> its pop-up too, with <paramref name="rowId"/> selected. Works in wallpaper mode as
    /// well: the editor stays in the tray and the wallpaper follows the changes.</summary>
    /// <param name="place">Rows to put on the calendar: opens "Put existing rows on the calendar" with them ticked.</param>
    internal void OpenCalendar(DateTime? date = null, string? rowId = null, bool openDay = false, IReadOnlyCollection<string>? place = null)
    {
        CommitFocusedEdit();
        if (_calendar == null)
        {
            var window = new CalendarWindow(CalendarModel, date);
            _calendar = window;
            window.Closed += (_, _) =>
            {
                if (_calendar != window) return;
                _calendar = null;
                _calendarTimer?.Stop();
            };
            window.Show();
            AppLog.Write("calendar", "calendar opened");
        }
        else
        {
            if (!_calendar.IsVisible) _calendar.Show();
            _calendar.Activate();
        }
        if (date is { } d)
        {
            var calendar = _calendar;
            // Posted: a window shown just now must be on screen before its pop-up can open over it.
            Dispatcher.UIThread.Post(() =>
            {
                if (_calendar != calendar) return;
                calendar.ShowDate(d, rowId, openDay && place == null);
                if (place != null) _ = calendar.OpenPlaceAsync(d, place);
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>The table changed: redraw the open calendar soon (one redraw for a run of changes, e.g. typing).</summary>
    private void NotifyCalendar()
    {
        if (_calendar == null) return;
        if (_calendarTimer == null)
        {
            _calendarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _calendarTimer.Tick += (_, _) =>
            {
                _calendarTimer.Stop();
                try { _calendarModel?.RaiseChanged(); }
                catch (Exception ex) { AppLog.Write("calendar", "refresh failed: " + ex.GetType().Name + ": " + ex.Message); }
            };
        }
        _calendarTimer.Stop();
        _calendarTimer.Start();
    }

    /// <summary>Redraws the open calendar now instead of after the short wait (tests).</summary>
    internal void FlushCalendarRefresh()
    {
        if (_calendarTimer is not { IsEnabled: true }) return;
        _calendarTimer.Stop();
        _calendarModel?.RaiseChanged();
    }

    private void CloseCalendar()
    {
        _calendarTimer?.Stop();
        var c = _calendar;
        _calendar = null;
        try
        {
            c?.Day?.Close(); // keeps what was being typed in it
            c?.Close();
        }
        catch { /* closing anyway */ }
    }

    private CalendarModel CalendarModel => _calendarModel ??= new CalendarModel
    {
        Items = (from, to, hideDone) => CalendarOps.Items(_session.Doc, from, to, DateTime.Now, hideDone),
        CanShow = () => CalendarOps.CanShow(_session.Doc),
        Columns = CalendarColumns,
        Cell = (rowId, columnId) => CalendarCell(rowId, columnId, out var r, out var c) ? _session.Doc.GetCell(r, c) : "",
        Show = (rowId, columnId) => CalendarCell(rowId, columnId, out var r, out var c) ? DaysCounter.CellText(_session.Doc, r, c, DateTime.Now) : "",
        SetCell = CalendarSetCell,
        TypeCell = CalendarTypeCell,
        AddRow = CalendarAddRow,
        SetTime = (rowId, time) => CalendarDueEdit(rowId, Localizer.T("date.undo_set"), row => CalendarOps.SetTime(_session.Doc, row, time, DateTime.Now)),
        Move = (rowId, date) => CalendarDueEdit(rowId, Localizer.T("undo.calendar_move"), row => CalendarOps.Move(_session.Doc, row, date, DateTime.Now)),
        ClearDate = rowId => CalendarDueEdit(rowId, Localizer.T("undo.calendar_clear"), row => CalendarOps.ClearDate(_session.Doc, row, DateTime.Now)),
        SetStatus = CalendarSetStatus,
        SetRepeat = CalendarSetRepeat,
        EditReminders = (rowId, owner) => EditRemindersAsync(rowId, owner),
        Delete = CalendarDeleteAsync,
        GoToRow = GoToRowFromCalendar,
        Undo = () =>
        {
            CommitFocusedEdit();
            var label = _undo.LastUndoLabel;
            if (label == null) return null;
            DoUndo();
            return label;
        },
        Redo = () =>
        {
            CommitFocusedEdit();
            var label = _undo.LastRedoLabel;
            if (label == null) return null;
            DoRedo();
            return label;
        },
        Unplaced = () => CalendarOps.Unplaced(_session.Doc),
        UnplacedCount = () => CalendarOps.UnplacedCount(_session.Doc),
        Place = CalendarPlace,
        AddDueColumn = CalendarAddDueColumn,
        Prefs = () => _settings.CalendarPrefs,
        SetPrefs = SetCalendarPrefs,
        OnWallpaper = () => _view == ViewMode.Background,
        Zoom = () => _settings.Scale / 100.0
    };

    private bool CalendarCell(string rowId, string columnId, out int row, out int column)
    {
        row = FindRowById(rowId);
        _session.Doc.EnsureColumnIds();
        column = _session.Doc.ColumnIds.IndexOf(columnId);
        return row >= 0 && column >= 0;
    }

    /// <summary>The table's columns as the day pop-up shows them: all but the due date (shown as the time) and the
    /// children of a collapsed group, in the table's order, at the table's widths (90–260).</summary>
    private List<CalendarColumn> CalendarColumns()
    {
        var doc = _session.Doc;
        var list = new List<CalendarColumn>();
        var due = doc.DueColumnIndex();
        var status = doc.StatusColumnIndex();
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            if (c == due || ColumnGroupOps.IsHidden(doc, c)) continue;
            var kind = c == status ? CalendarColumn.Status : ColumnSettingsOps.TypeOf(doc, c);
            var options = kind == ColumnTypes.Choice ? ColumnSettingsOps.Get(doc, c).Options : [];
            var name = doc.Columns[c];
            if (string.IsNullOrWhiteSpace(name)) name = Localizer.T("delete.column_fallback", c + 1);
            list.Add(new CalendarColumn(doc.ColumnIdAt(c), name, kind, options, Math.Clamp(doc.ColumnWidth(c), 90, 260)));
        }
        return list;
    }

    /// <summary>After a change from the calendar: the row is drawn again without moving the focus (the user is typing in
    /// the calendar, not in the table); a change that adds or hides rows rebuilds the table.</summary>
    private void RefreshFromCalendar(int row, bool structural)
    {
        CommitFocusedEdit();
        if (structural || _session.Doc.HideDone) { Rebuild(); return; }
        foreach (var c in RowsList.GetRealizedContainers())
        {
            if (c is not ContentPresenter { Child: Grid { Tag: int r } g } || r != row) continue;
            _ignoreText = true;
            try { PopulateRow(g, row); }
            finally { _ignoreText = false; }
        }
        NotifyCalendar();
    }

    /// <summary>A cell of the day pop-up is kept: the table gets the text (already there when it was typed) and one undo
    /// step from <paramref name="before"/>, the text the cell had when the pop-up drew it.</summary>
    private bool CalendarSetCell(string rowId, string columnId, string before, string value)
    {
        if (!CalendarCell(rowId, columnId, out var row, out var column)) return false;
        var doc = _session.Doc;
        if (column == doc.StatusColumnIndex()) return CalendarSetStatus(rowId, TaskStatuses.DisplayCode(value));
        if (column == doc.DueColumnIndex()) return false; // the time box and Move to change the date
        if (doc.GetCell(row, column) != value && !_session.SetCell(row, column, value))
        {
            StatusText.Text = TableLimits.CellTooLongMessage;
            return false;
        }
        if (before == value) return true;
        _undo.Push(CellEditAction.For(doc, rowId, column, before, value));
        ScheduleSave();
        RefreshFromCalendar(row, structural: false);
        _reminders.CheckNow();
        return true;
    }

    /// <summary>Typing in a cell of the day pop-up: into the table at once and saved soon, like typing in the table
    /// (the undo step is pushed when the cell is kept, <see cref="CalendarSetCell"/>).</summary>
    private bool CalendarTypeCell(string rowId, string columnId, string value)
    {
        if (!CalendarCell(rowId, columnId, out var row, out var column)) return false;
        var doc = _session.Doc;
        if (column == doc.StatusColumnIndex() || column == doc.DueColumnIndex()) return false;
        if (doc.GetCell(row, column) == value) return true;
        if (!_session.SetCell(row, column, value)) return false;
        ScheduleSave();
        return true;
    }

    /// <summary>A due date edit from the calendar (time, move, remove the date): one undo step on the due cell.</summary>
    private bool CalendarDueEdit(string rowId, string label, Func<int, bool> change)
    {
        var doc = _session.Doc;
        var row = FindRowById(rowId);
        var due = doc.DueColumnIndex();
        if (row < 0 || due < 0) return false;
        var before = doc.GetCell(row, due);
        if (!change(row)) return false;
        _undo.Push(CellEditAction.For(doc, rowId, due, before, doc.GetCell(row, due), label));
        ScheduleSave();
        RefreshFromCalendar(row, structural: false);
        _reminders.CheckNow();
        return true;
    }

    /// <summary>Same rules as the table's status drop-down: Done adds the next round of a repeating task, a done task
    /// opened again reminds from now.</summary>
    private bool CalendarSetStatus(string rowId, string code)
    {
        var doc = _session.Doc;
        var row = FindRowById(rowId);
        var column = doc.StatusColumnIndex();
        if (row < 0 || column < 0) return false;
        var prev = doc.GetCell(row, column);
        if (TaskStatuses.DisplayCode(prev) == code) return true;
        if (TaskStatuses.IsDone(code) && !TaskStatuses.IsDone(prev))
        {
            var before = Snapshot.CloneLite(doc);
            var rowsBefore = doc.Rows.Count;
            var msg = Recurrence.CompleteAndSpawnNext(doc, row);
            doc.Acknowledged.Remove(rowId);
            _undo.Push(new SnapshotAction(Localizer.T("undo.change_status"), before, Snapshot.CloneLite(doc)));
            ScheduleSave();
            RefreshFromCalendar(row, structural: doc.Rows.Count != rowsBefore);
            StatusText.Text = msg ?? Localizer.T("ui.marked_done");
            _reminders.CheckNow();
            return true;
        }
        if (!_session.SetCell(row, column, code)) return false;
        _undo.Push(CellEditAction.For(doc, rowId, column, prev, code, Localizer.T("undo.change_status")));
        if (TaskStatuses.IsDone(prev) && !TaskStatuses.IsDone(code)) ReminderPlan.Reset(doc, rowId, DateTime.Now);
        ScheduleSave();
        RefreshFromCalendar(row, structural: false);
        _reminders.CheckNow();
        return true;
    }

    private bool CalendarSetRepeat(string rowId, int index)
    {
        var doc = _session.Doc;
        var row = FindRowById(rowId);
        if (row < 0) return false;
        var old = doc.Recurrence.TryGetValue(rowId, out var v) ? v : null;
        if (Recurrence.IndexOf(old) == index) return true;
        var dueCol = doc.DueColumnIndex();
        var anchor = dueCol >= 0 && DueDates.TryParse(doc.GetCell(row, dueCol), out var d) ? d.Date : DateTime.Today;
        var kind = Recurrence.FromIndex(index, anchor);
        var before = Snapshot.CloneLite(doc);
        if (kind == Recurrence.None) doc.Recurrence.Remove(rowId);
        else doc.Recurrence[rowId] = kind;
        _undo.Push(new SnapshotAction(Localizer.T("undo.set_repeat"), before, Snapshot.CloneLite(doc)));
        ScheduleSave();
        RefreshFromCalendar(row, structural: false);
        return true;
    }

    /// <summary>+ Row in a day's pop-up: a row due that day (one undo step). The empty rows at the end of the table are
    /// used first; a row with a picture in a cell is not empty.</summary>
    private string? CalendarAddRow(DateTime date)
    {
        var doc = _session.Doc;
        if (!CalendarOps.CanShow(doc)) return null;
        CommitFocusedEdit();
        var before = Snapshot.CloneLite(doc);
        var now = DateTime.Now;
        var row = CalendarOps.AddRow(doc, date, CalendarOps.DefaultTimeFor(date, now), now, RowHasCellPicture);
        if (row < 0)
        {
            StatusText.Text = TableLimits.RowLimitMessage;
            return null;
        }
        _undo.Push(new SnapshotAction(Localizer.T("undo.calendar_add"), before, Snapshot.CloneLite(doc)));
        Rebuild();
        FlushSave();
        StatusText.Text = Localizer.T("calendar.added_status", row + 1, CalendarOps.ShortDate(date));
        _reminders.CheckNow();
        return doc.RowIdAt(row);
    }

    /// <summary>Rows written before the calendar (or without a date) put on days: one undo step.</summary>
    private int CalendarPlace(IReadOnlyList<(string RowId, DateTime Date, TimeSpan? Time)> rows)
    {
        var doc = _session.Doc;
        if (!CalendarOps.CanShow(doc) || rows.Count == 0) return 0;
        CommitFocusedEdit();
        var before = Snapshot.CloneLite(doc);
        var n = CalendarOps.Place(doc, rows, DateTime.Now);
        if (n == 0) return 0;
        _undo.Push(new SnapshotAction(Localizer.T("undo.calendar_place"), before, Snapshot.CloneLite(doc)));
        Rebuild();
        FlushSave();
        StatusText.Text = Localizer.T(n == 1 ? "calendar.placed_one" : "calendar.placed", n);
        ActivityLog.Shared.Add(ActivityLevel.Done, StatusText.Text);
        AppLog.Write("calendar", $"{n} rows put on the calendar");
        _reminders.CheckNow();
        return n;
    }

    /// <summary>The calendar's "Add the due date column" (the table has none): as Table → Add alert-date column, one undo
    /// step, also while the table is on the wallpaper.</summary>
    private bool CalendarAddDueColumn()
    {
        var doc = _session.Doc;
        if (CalendarOps.CanShow(doc)) return true;
        CommitFocusedEdit();
        var before = Snapshot.CloneLite(doc);
        var side = UndoSideState.Capture(doc);
        var err = _session.AddSystemColumn(ColumnRoles.Due);
        if (err != null)
        {
            StatusText.Text = err;
            return false;
        }
        _undo.Push(new SnapshotAction(Localizer.T("undo.add_column"), before, Snapshot.CloneLite(doc), side.RemovedBy(doc)));
        Rebuild();
        FlushSave();
        _reminders.CheckNow();
        StatusText.Text = Localizer.T("col.added_due");
        return CalendarOps.CanShow(doc);
    }

    private bool RowHasCellPicture(string rowId) =>
        _ink.CellImages.Keys.Any(k => CellDetailOps.TrySplit(k, out var r, out _) && r == rowId);

    /// <summary>− Row in a day's pop-up: deletes the row from the table (asks when it has text), one undo step.</summary>
    private async Task<bool> CalendarDeleteAsync(string rowId, Window owner)
    {
        var doc = _session.Doc;
        var row = FindRowById(rowId);
        if (row < 0) return false;
        if (doc.RowHasContent(row) && !await MessageBox.Show(owner, Localizer.T("delete.row_has_text"), Localizer.T("delete.title")))
            return false;
        row = FindRowById(rowId); // the table may have changed while the question was open
        if (row < 0) return false;
        CommitFocusedEdit();
        var undo = RowDeleteAction.Capture(doc, row);
        if (!doc.RemoveRow(row)) return false;
        _undo.Push(undo);
        if (_session.HasSelection)
        {
            if (doc.Rows.Count == 0) _session.ClearSelection();
            else if (_session.ActiveRow >= row) _session.ChooseCell(Math.Clamp(_session.ActiveRow - (_session.ActiveRow > row ? 1 : 0), 0, doc.Rows.Count - 1), Math.Max(0, _session.ActiveCol));
        }
        Rebuild();
        FlushSave();
        StatusText.Text = Localizer.T("calendar.deleted");
        _reminders.CheckNow();
        return true;
    }

    /// <summary>↗ in the calendar: the editor comes forward (from the tray or the wallpaper too) with the row selected.
    /// A done row hidden by Hide done rows is shown again.</summary>
    private void GoToRowFromCalendar(string rowId)
    {
        var row = FindRowById(rowId);
        if (row < 0) return;
        ShowEditor();
        var doc = _session.Doc;
        var status = doc.StatusColumnIndex();
        if (doc.HideDone && status >= 0 && TaskStatuses.IsDone(doc.GetCell(row, status)))
            HideDoneBox.IsChecked = false; // OnHideDoneChanged rebuilds with the row in it
        Dispatcher.UIThread.Post(() =>
        {
            var r = FindRowById(rowId);
            if (r < 0) return;
            var title = doc.IndexOfRole(ColumnRoles.Title);
            var column = title >= 0 && !ColumnGroupOps.IsHidden(doc, title) ? title : FirstVisibleColumn();
            FocusCell(r, column);
            Highlight();
            Activate();
            StatusText.Text = Localizer.T("calendar.went_to_row", r + 1);
        }, DispatcherPriority.Background);
    }

    private void SetCalendarPrefs(CalendarPrefs prefs)
    {
        var before = _settings.CalendarPrefs;
        _settings.CalendarWallpaper = prefs.Wallpaper;
        _settings.CalendarWeekStart = CalendarOps.NormalizeWeekStart(prefs.WeekStart);
        _settings.CalendarHideDone = prefs.HideDone;
        if (_settings.CalendarPrefs == before) return;
        _settings.Save(_path);
        if (before.Wallpaper != prefs.Wallpaper)
        {
            var text = Localizer.T(prefs.Wallpaper switch
            {
                CalendarWallpaper.Corner => "calendar.wall_corner_on",
                CalendarWallpaper.Only => "calendar.wall_only_on",
                _ => "calendar.wall_off_on"
            });
            StatusText.Text = text;
            ActivityLog.Shared.Add(ActivityLevel.Info, text);
            AppLog.Write("calendar", "on the wallpaper: " + prefs.Wallpaper);
        }
        ScheduleWallpaperRefresh(); // in wallpaper mode the picture changes now
        NotifyCalendar();
    }

    /// <summary>A row's right-click menu: Show in calendar (a row with a due date), or Put on the calendar… (a row with
    /// text and no date that can be read: "Put existing rows on the calendar" opens with it, and the other selected rows
    /// like it, ticked).</summary>
    private IEnumerable<Control> CalendarRowMenu(string rowId)
    {
        var doc = _session.Doc;
        var row = doc.RowIds.IndexOf(rowId);
        if (row < 0) yield break;
        var due = doc.DueColumnIndex();
        DateTime when = default;
        var has = due >= 0 && DueDates.TryParse(doc.GetCell(row, due), out when);
        if (has)
        {
            var show = new MenuItem { Header = Localizer.T("calendar.show_row"), FontFamily = AppFonts.Thai, IsEnabled = _view == ViewMode.Edit };
            ToolTip.SetTip(show, Localizer.T("calendar.show_row_tip"));
            var date = when.Date;
            show.Click += (_, _) => OpenCalendar(date, rowId, openDay: true);
            yield return show;
            yield break;
        }
        var put = new MenuItem { Header = Localizer.T("calendar.put_row"), FontFamily = AppFonts.Thai, IsEnabled = _view == ViewMode.Edit && doc.RowHasContent(row) };
        ToolTip.SetTip(put, Localizer.T("calendar.put_row_tip"));
        put.Click += (_, _) =>
        {
            var ids = RowsFor(doc.RowIds.IndexOf(rowId)).Select(doc.RowIdAt).Append(rowId).Distinct().ToList();
            OpenCalendar(DateTime.Today, place: ids);
        };
        yield return put;
    }
}
