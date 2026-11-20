using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>2.6.0: days counter column. The cell shows the days since the start date; clicking it opens
/// <see cref="DaysDialog"/> with the row history. History is captured on every autosave (<see cref="CaptureHistory"/>).</summary>
public partial class MainWindow
{
    private readonly RowHistoryTracker _history = new();
    private DateTime _daysDate = DateTime.Today;
    private static readonly IBrush DaysPlaceholderFg = new SolidColorBrush(Color.Parse("#A08E5C"));

    /// <summary>Writes the cell changes since the last look into the row history (tables with a days column only).</summary>
    private void CaptureHistory()
    {
        try { _history.Capture(_session.Doc, DateTime.Now); }
        catch (Exception ex) { AppLog.Write("history", "capture failed: " + ex.GetType().Name + ": " + ex.Message); }
    }

    private Control MakeDaysCell(int row, int column, IBrush bg)
    {
        var doc = _session.Doc;
        var raw = doc.GetCell(row, column);
        var count = DaysCounter.ForCell(doc, row, column, DateTime.Now);
        var blank = count.State == DaysState.Blank;
        var done = count.State is DaysState.Done or DaysState.DoneUnknown;
        var label = new TextBlock
        {
            Text = blank ? Localizer.T("days.blank") : DaysCounter.CellText(count, raw),
            FontFamily = AppFonts.Thai,
            FontSize = 14,
            FontStyle = blank ? FontStyle.Italic : FontStyle.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var cell = new Button
        {
            Content = label,
            Background = bg,
            // On the button, not the text: a cell text colour (Colors…) is painted onto the button and inherited.
            Foreground = count.State == DaysState.Invalid ? InvalidNumberFg : blank ? DaysPlaceholderFg : done ? DoneFg : TextFg,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(6, 4),
            MinWidth = 96,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsHitTestVisible = _view == ViewMode.Edit,
            Tag = "days"
        };
        if (done) cell.Opacity = 0.75;
        var tip = count.State == DaysState.Invalid ? Localizer.T("days.invalid_tip", raw.Trim()) : Localizer.T("days.cell_tip");
        if (RowHistoryOps.LastUpdate(doc, doc.RowIdAt(row)) is { } last) // "has anything happened?" without opening it
            tip = Localizer.T("days.last_update", DaysCounter.DateTimeText(last), DaysCounter.Ago(last, DateTime.Now)) + "\n" + tip;
        ToolTip.SetTip(cell, tip);
        var shift = false;
        // Button marks the press handled, so the selection is taken in the tunnel phase.
        cell.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_view != ViewMode.Edit) return;
            shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (shift) _session.ExtendTo(row, column);
            else _session.ChooseCell(row, column);
            Highlight();
            UpdateStatus();
        }, RoutingStrategies.Tunnel);
        cell.GotFocus += (_, e) => { if (_view == ViewMode.Edit) OnCellFocused(row, column, e.KeyModifiers); };
        var rowId = doc.RowIdAt(row);
        var columnId = doc.ColumnIdAt(column);
        cell.Click += (_, _) =>
        {
            if (shift) { shift = false; return; } // Shift+click extends the selection only
            _ = OpenDaysAsync(rowId, columnId);
        };
        AttachEnterNavigation(cell, row, column); // Enter moves like every cell; click / Space opens
        return cell;
    }

    /// <summary>Title of the pop-up: the first text of the row (else "Row n").</summary>
    private string RowHeading(int row)
    {
        var doc = _session.Doc;
        var skip = new HashSet<int> { doc.StatusColumnIndex(), doc.DueColumnIndex() };
        foreach (var c in DaysCounter.ColumnIndexes(doc)) skip.Add(c);
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            if (skip.Contains(c)) continue;
            var t = doc.GetCell(row, c).Trim();
            if (t.Length > 0) return RowHistoryOps.Clip(t, 90);
        }
        return Localizer.T("days.row_n", row + 1);
    }

    private async Task OpenDaysAsync(string rowId, string columnId)
    {
        if (_busy || _view != ViewMode.Edit) return;
        CommitFocusedEdit();
        CaptureHistory(); // a change typed just before the click is in the list already
        var doc = _session.Doc;
        var row = FindRowById(rowId);
        var column = doc.ColumnIds.IndexOf(columnId);
        if (row < 0 || column < 0) return;
        _busy = true;
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double? room = screen == null ? null : screen.WorkingArea.Height / Math.Max(0.5, screen.Scaling) - 40;
            var dialog = new DaysDialog(RowHeading(row), new DaysDialog.Model
            {
                Info = () =>
                {
                    var r = FindRowById(rowId);
                    var c = _session.Doc.ColumnIds.IndexOf(columnId);
                    return r < 0 || c < 0 || !DaysCounter.IsDaysColumn(_session.Doc, c) ? null : DaysCounter.Describe(_session.Doc, r, c, DateTime.Now);
                },
                SetStart = date =>
                {
                    var r = FindRowById(rowId);
                    var c = _session.Doc.ColumnIds.IndexOf(columnId);
                    if (r < 0 || c < 0) return false;
                    SetTypedCell(r, c, DaysCounter.TodayText(date), Localizer.T("days.undo_start"));
                    CaptureHistory();
                    return true;
                },
                AddNote = text =>
                {
                    if (RowHistoryOps.AddNote(_session.Doc, rowId, text, DateTime.Now) == null) return false;
                    ScheduleSave();
                    StatusText.Text = Localizer.T("days.note_added");
                    AppLog.Write("history", "progress note added");
                    return true;
                },
                EditNote = (e, text) =>
                {
                    if (!RowHistoryOps.EditNote(_session.Doc, rowId, e, text, DateTime.Now)) return false;
                    ScheduleSave();
                    StatusText.Text = Localizer.T("days.note_changed");
                    AppLog.Write("history", "progress note edited");
                    return true;
                },
                RemoveNote = e =>
                {
                    if (!RowHistoryOps.Remove(_session.Doc, rowId, e)) return false;
                    ScheduleSave();
                    AppLog.Write("history", "progress note removed");
                    return true;
                }
            }, _settings.Scale / 100.0, room);
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppLog.Write("history", "days pop-up failed: " + ex.GetType().Name + ": " + ex.Message);
            StatusText.Text = Localizer.T("ui.action_failed", ex.Message);
        }
        finally { _busy = false; }
        FlushSave();
        var at = FindRowById(rowId);
        if (at >= 0) RefreshRow(at);
    }

    /// <summary>Table → Add days counter column: a new "Days" column just before the status column (the reminder
    /// date editor is wider than its column and runs on to the right, so it stays last); open rows start today.</summary>
    private Task AddDaysColumnAsync()
    {
        if (_view != ViewMode.Edit) return Task.CompletedTask;
        var doc = _session.Doc;
        var err = _session.AddColumn();
        if (err != null) { StatusText.Text = err; return Task.CompletedTask; }
        var column = doc.Columns.Count - 1;
        var name = Localizer.T("days.default_name");
        for (var i = 2; doc.Columns.Take(column).Contains(name); i++) name = Localizer.T("days.default_name") + " " + i;
        ColumnSettingsOps.Apply(doc, column, name, ColumnTypes.Days, null, null);
        var before = new[] { doc.StatusColumnIndex(), doc.DueColumnIndex() }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (before >= 0 && before < column && !ColumnGroupOps.IsChild(doc, before) && !ColumnGroupOps.IsParent(doc, before) &&
            doc.MoveColumn(column, before)) column = before;
        _session.ChooseCell(Math.Max(0, _session.ActiveRow), column);
        DaysCounter.StartToday(doc, column, DateTime.Now);
        Rebuild();
        FocusActiveCell();
        FlushSave();
        StatusText.Text = Localizer.T("days.added");
        AppLog.Write("table", $"days column added at {column + 1}");
        return Task.CompletedTask;
    }

    /// <summary>Minute clock: after midnight the counters of the rows on screen move on by a day.</summary>
    private bool DaysDateChanged()
    {
        if (DateTime.Today == _daysDate) return false;
        _daysDate = DateTime.Today;
        return DaysCounter.HasColumn(_session.Doc);
    }
}
