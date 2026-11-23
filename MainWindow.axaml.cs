using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TableNote;

public partial class MainWindow : Window
{
    private static readonly IBrush HeaderBg = new SolidColorBrush(Color.Parse("#F6E08A"));
    private static readonly IBrush CellBg = new SolidColorBrush(Color.Parse("#FFFDF6"));
    private static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#FFE08A"));
    private static readonly IBrush RowNumBg = new SolidColorBrush(Color.Parse("#F8E7A4"));
    private static readonly IBrush GridLine = new SolidColorBrush(Color.Parse("#E4D08A"));
    private static readonly IBrush ActiveBorder = new SolidColorBrush(Color.Parse("#C47B2B"));
    private static readonly IBrush TextFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush DoneFg = new SolidColorBrush(Color.Parse("#9A9080"));
    private static readonly IBrush OverdueBg = new SolidColorBrush(Color.Parse("#FFD0C8"));
    private static readonly IBrush TodayBg = new SolidColorBrush(Color.Parse("#FFE9A8"));
    private static readonly IBrush DoneBg = new SolidColorBrush(Color.Parse("#EFEADF"));
    // v2.3.1: shared instead of one new brush / cursor per cell / header.
    private static readonly IBrush StrikeFg = new SolidColorBrush(Color.Parse("#9A9080"));
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static List<string>? _statusLabels;
    private static string? _statusLabelsLang;
    private static List<string> StatusLabels()
    {
        if (_statusLabels == null || _statusLabelsLang != Localizer.Current)
        {
            _statusLabels = TaskStatuses.Codes.Select(TaskStatuses.Label).ToList();
            _statusLabelsLang = Localizer.Current;
        }
        return _statusLabels;
    }

    private readonly string _path;
    private TableSession _session; // replaced once when note.json opens after a stand-in table (MainWindow.Reload.cs)
    private readonly DialogDeleteConfirm _confirm;
    private readonly DispatcherTimer _saveTimer;
    private readonly IStartupRegistration _startup;
    private string? _loadWarning;
    private readonly ReminderService _reminders;
    private readonly UndoStack _undo = new();
    private readonly List<Control> _headerBoxes = [];

    private bool _tracking;
    private bool _busy;
    private bool _ignoreText;
    private bool _preserveExistingFile;
    private string? _preserveBaseline; // v2.4.1: document as loaded, to know whether the session has edits
    private string? _saveFailureMessage;
    private bool _applyingPlacement;
    /// <summary>Edit = the editor window; Background = wallpaper mode (the table is the desktop wallpaper, the editor is hidden).</summary>
    private ViewMode _view;
    private bool _trayReady;
    private bool _hotkeyReady;
    private bool _hotkeyCollided;
    private TrayIcon? _tray;

    public MainWindow()
    {
        InitializeComponent();
        _path = NoteStore.DefaultPath();
        UiWatchdog.Start(); // logs to tablenote.log when the UI thread stalls > 2 s
        // UI language + zoom live in settings.json (never note.json). Missing/bad → English, 100 %.
        _settings = AppSettings.Load(_path);
        Localizer.SetLanguage(_settings.Language);
        ApplySoundSettings(); // 2.6.0: reminder sound and volume
        // Before anything can write a new side copy: keep an earlier run's note.unsaved.json under its own name.
        _archivedUnsaved = NoteStore.ArchiveUnsaved(_path);
        var loaded = NoteStore.Load(_path);
        _session = new TableSession(loaded.Document);
        _history.Reset(_session.Doc); // 2.6.0: days counter history starts from the loaded note
        _placeholder = loaded.Placeholder;
        _preserveExistingFile = loaded.PreserveExistingFile;
        if (_preserveExistingFile) _preserveBaseline = PreserveKey(_session.Doc);
        _startup = StartupLaunch.ForCurrentMachine();
        // A stand-in table (note.json locked) has "launch at sign-in" off only because it is a default document:
        // syncing that deleted the user's sign-in entry, so TableNote did not start at the next sign-in either.
        var startupMsg = _placeholder ? null : SyncStartup(_session.Doc.LaunchAtLogin);
        _loadWarning = loaded.Warning ?? startupMsg;
        _confirm = new DialogDeleteConfirm(this);
        InitSettings(); // v2.4.0: column settings item in the header menu
        InitDetailPopup(); // wraps settings, so detail items stay beside them
        InitSizing(); // wraps that hook again: Column width… / Row height… plus the detail items
        InitAlertBoxes();

        MinWidth = 480;
        MinHeight = 300;
        Width = Math.Max(720, _session.Doc.Window.Width);
        Height = Math.Max(480, _session.Doc.Window.Height);
        Topmost = DesktopPlacement.EffectiveTopmost(_session.Doc.Window.Layer);
        TopmostBox.IsChecked = _session.Doc.Window.Layer == DesktopLayerKind.Floating;
        LockBox.IsChecked = _session.Doc.Window.Locked;
        LaunchBox.IsChecked = _session.Doc.LaunchAtLogin;
        HideDoneBox.IsChecked = _session.Doc.HideDone;
        MuteSoundBox.IsChecked = _session.Doc.MuteSound;

        if (_session.Doc.Window.X is int sx && _session.Doc.Window.Y is int sy)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(sx, sy);
        }

        _saveTimer = new DispatcherTimer { Interval = AutosaveDebounce.Delay };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); DoSave(); };

        _reminders = new ReminderService(() => _session.Doc, OnDueBatch);
        NotificationSound.Muted = _session.Doc.MuteSound;

        // The toolbar keeps the everyday commands; copy, image export, print, delete column, language, help,
        // and the system settings are in the menu bar (MainWindow.MenuBar.cs).
        AddRowButton.Click += async (_, _) => await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_row"), AddRowAsync));
        DeleteRowButton.Click += async (_, _) => await RunStructureAsync(() => DeleteRowsAsync(-1)); // 2.6.0: every selected row
        InitColumnToggle();
        SortDueButton.Click += (_, _) => SortByDue();
        ExportButton.Click += async (_, _) => await ExportAsync();
        InitWallpaper();
        InitSketch(); // 2.6.1.1
        ZoomOutButton.Click += (_, _) => SetZoom(_settings.Scale - AppSettings.ScaleStep);
        ZoomInButton.Click += (_, _) => SetZoom(_settings.Scale + AppSettings.ScaleStep);
        ZoomResetButton.Click += (_, _) => SetZoom(100);
        AddHandler(InputElement.PointerWheelChangedEvent, OnZoomWheel, RoutingStrategies.Tunnel);
        Localizer.LanguageChanged += ApplyLanguage;
        TopmostBox.PropertyChanged += OnTopmostBoxChanged;
        LockBox.PropertyChanged += OnLockBoxChanged;
        LaunchBox.PropertyChanged += OnLaunchBoxChanged;
        HideDoneBox.PropertyChanged += OnHideDoneChanged;
        MuteSoundBox.PropertyChanged += OnMuteSoundChanged;
        PositionChanged += OnPositionChanged;
        Resized += OnResized;
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        TableScroll.SizeChanged += (_, _) => FitColumns();
        Program.EditRequested += () => Dispatcher.UIThread.Post(ShowEditor);
        ToolTip.SetTip(StatusText, Localizer.T("ui.saved_path", _path));

        InitColors();
        InitExcelSelection(); // 2.6.0
        InitFormulaBar();
        InitFind();
        InitActivityBar(); // 2.6.0: what TableNote is doing (bottom bar)
        ApplyZoom();
        InitMenuBar();
        ApplyLanguageTexts();
        Rebuild();
        UpdateStatus();
        if (_placeholder)
            ShowNotice(Localizer.T("load.locked_waiting")); // MainWindow.Reload.cs keeps trying to open note.json
        else if (!string.IsNullOrEmpty(_session.Doc.DueColumnWarning))
            ShowNotice(_session.Doc.DueColumnWarning);
        else if (!string.IsNullOrEmpty(loaded.Notice))
            ShowNotice(loaded.Notice); // non-blocking banner; details in tablenote.log
        if (!string.IsNullOrEmpty(loaded.Notice))
            ToolTip.SetTip(StatusText, Localizer.T("ui.saved_path_log", _path,
                Path.Combine(Path.GetDirectoryName(_path) ?? ".", "tablenote.log")));
    }

    private bool _startedUp;
    private bool _startedUpEditor; // the first EnterEdit logs its first frame; later ones only a window that is never drawn

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampIfPlaced();
        _tracking = true;
        FitColumns();
        // Avalonia raises Opened on EVERY Show(), also when the editor comes back from the tray. Up to 2.5.1 the
        // start-up below ran again then: after a sign-in start (--startup) it hid the editor again at once, and each
        // return added another window hook and Screens handler. Start-up runs once.
        if (_startedUp) return;
        _startedUp = true;
        StartClock.Mark("editor opened");
        LogStart(); // 2.6.0: one line per start in tablenote.log
        NoteStart(); // 2.6.0: "opened the table: N rows (x s)" — the first line of the activity bar's History
        if (Screens != null)
            Screens.Changed += (_, _) => OnDisplayChanged();

        using (UiActivity.Scope("start: tray and shortcut"))
        {
            InstallShellHook();
            // v2.4.2: the taskbar button now follows the mode (EnterEdit / EnterWallpaperView set it).
            _trayReady = TryCreateTray();
            _hotkeyReady = TryRegisterHotkey();
        }

        var routes = CurrentRoutes();
        var notice = RecoveryGate.StartupNotice(_session.Doc.View, Program.ForceEdit, routes);
        var mode = RecoveryGate.ResolveStartup(_session.Doc.View, Program.ForceEdit, routes);
        if (!_hotkeyReady && OperatingSystem.IsWindows()) // the wallpaper tip leaves the shortcut out then
            AppLog.Write("hotkey", _hotkeyCollided ? "Ctrl+Shift+E is used by another program" : "Ctrl+Shift+E could not be registered");

        if (mode == ViewMode.Background)
        {
            // Wallpaper mode was on: draw the table again (dates may have moved on). wallpaper.json still has the
            // user's own wallpaper, so it is not mistaken for the table picture that is up now.
            var outcome = ApplyWallpaper(confirmed: false);
            if (outcome.Ok) EnterWallpaperView();
            else EnterEdit(outcome.NeedsConfirm ? Localizer.T("wallpaper.spotlight_start") : outcome.Message);
        }
        else if (Program.StartHidden && _trayReady)
        {
            // Login start: stay hidden in the tray; reminders still run.
            // The editor gets its taskbar button when it is shown (tray / hotkey / second start → EnterEdit).
            using (UiActivity.Scope("start: restore wallpaper"))
                RestorePendingWallpaper(); // a table picture left by a crash: put the user's wallpaper back
            Dispatcher.UIThread.Post(Hide);
        }
        else EnterEdit(notice);

        // After the mode is set: entering the editor's "normal window" layer clears the banner.
        if (_placeholder)
        {
            var waiting = Localizer.T("load.locked_waiting");
            if (!(ModeNotice.Text ?? "").Contains(waiting, StringComparison.Ordinal)) ShowNotice(JoinNotice(ModeNotice.Text, waiting));
            StartReloadRetry();
        }
        else if (_archivedUnsaved is { } kept)
            ShowNotice(JoinNotice(ModeNotice.Text, UnsavedKeptNotice(kept)));

        // 2.6.0: like Excel, the editor opens with the first cell selected (not the formula bar).
        Dispatcher.UIThread.Post(() =>
        {
            if (_view == ViewMode.Edit && IsVisible && !_session.HasSelection && _visibleRows.Count > 0)
                FocusCell(_visibleRows[0], FirstVisibleColumn());
        }, DispatcherPriority.Background);
        _reminders.Start();
        StartColourClock();
        // Quick first check shortly after open so test due-in-1-min fires soon
        var kick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        kick.Tick += (_, _) => { kick.Stop(); _reminders.CheckNow(); };
        kick.Start();
        StartClock.Mark("editor ready");
    }

    private bool _quitting;

    /// <summary>Tray "Exit": the only real exit. Saves, then shuts the app down.</summary>
    private void QuitApp()
    {
        _quitting = true;
        // Wallpaper mode stays on in note.json (the next start draws the table again), but the user's own wallpaper
        // goes back now, so an exited TableNote never leaves an out-of-date table on the desktop.
        if (_view == ViewMode.Background) RestoreWallpaperOnExit();
        Close();
        if (!_quitting) return; // OnClosing cancelled because save failed
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private void HideToTray()
    {
        if (_tracking) { CaptureBounds(); FlushSave(); }
        Hide();
        if (!_session.Doc.TrayNoticeShown)
        {
            _session.Doc.TrayNoticeShown = true;
            FlushSave();
            ShowTrayNotice();
        }
    }

    private static void ShowTrayNotice()
    {
        var ok = new Button { Content = Localizer.T("ui.got_it"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var w = new Window
        {
            Title = Localizer.T("ui.tray_notice_title"),
            Width = 400, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = AppFonts.Thai,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, FontSize = 14,
                        Text = Localizer.T("ui.tray_notice_body", Localizer.T("tray.edit"), Localizer.T("tray.quit"))
                    },
                    ok
                }
            }
        };
        ok.Click += (_, _) => w.Close();
        w.Show();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the main window hides to the tray so reminders keep running; quit only from the tray.
        // v2.3.1 fix: Windows sign-out / shutdown must not be cancelled (it was hidden to the tray instead,
        // which can hold up "This app is preventing shutdown"); save and let it close.
        var systemClose = e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown;
        if (!_quitting && _trayReady && !systemClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (_tracking) CaptureBounds();
        if (!FlushSave() && !systemClose)
        {
            e.Cancel = true;
            _quitting = false;
            if (_view == ViewMode.Background) EnterEdit(null); // the table picture goes, the editor opens
            else if (!IsVisible) Show();
            ShowSaveFailedOnQuit();
            return;
        }
        // Signing out / shutting down in wallpaper mode: with "launch at sign-in" TableNote redraws the table at the
        // next sign-in, so the picture stays; otherwise the user's wallpaper goes back now.
        if (systemClose && _view == ViewMode.Background && !_session.Doc.LaunchAtLogin) RestoreWallpaperOnExit();
        CloseSketch(); // 2.6.1.1: the drawing pad is saved and closes with TableNote
        _wallpaperTimer?.Stop();
        WindowsHotkey.Unregister(WindowHandle());
        _reminders.Dispose();
        _reloadTimer?.Stop();
        ReminderPopup.CloseSilently(); // v2.3.1: no popup left calling into a closed window
        base.OnClosing(e);
        // Without a tray icon this close is the exit (README). The app runs with ShutdownMode.OnExplicitShutdown, so
        // before, the closed window left an invisible process holding the single-instance lock: starting TableNote
        // again did nothing until that process was ended in Task Manager.
        if (!e.Cancel && !_quitting && !systemClose)
        {
            _quitting = true;
            if (ExitAfterCloseOverride is { } exit) exit();
            else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
                Dispatcher.UIThread.Post(() => life.Shutdown());
        }
    }

    /// <summary>Test seam: replaces "shut TableNote down" after the window closed without a tray icon.</summary>
    internal static Action? ExitAfterCloseOverride { get; set; }

    private void ShowSaveFailedOnQuit()
    {
        var text = _saveFailureMessage ?? Localizer.T("ui.save_failed");
        var ok = new Button { Content = Localizer.T("ui.got_it"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var w = new Window
        {
            Title = Localizer.T("ui.save_failed_title"),
            Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = AppFonts.Thai,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap, FontSize = 14,
                        Text = Localizer.T("ui.save_failed_body", text)
                    },
                    ok
                }
            }
        };
        ok.Click += (_, _) => w.Close();
        w.Show();
    }

    private void OnDueBatch(IReadOnlyList<DueTask> tasks)
    {
        // 2.6.0: History shows which reminders came up.
        var titles = string.Join(", ", tasks.Take(2).Select(t => "\"" + RowHistoryOps.Clip(string.IsNullOrWhiteSpace(t.Title) ? Localizer.T("days.row_n", t.RowIndex + 1) : t.Title, 40) + "\""));
        if (tasks.Count > 2) titles += " …";
        ActivityLog.Shared.Add(ActivityLevel.Info, tasks.Count == 1 ? Localizer.T("activity.reminded_one", titles) : Localizer.T("activity.reminded", tasks.Count, titles));
        // v2.4.2: window could not be shown → say so in the status bar (tasks remind again on the next check).
        ReminderPopup.ShowFailed = n => StatusText.Text = Localizer.T("notify.popup_failed", n);
        ReminderPopup.ShowBatch(
            tasks,
            snoozeOne: task => ApplySnooze(task),
            doneOne: task => ApplyDone(task.RowId),
            acknowledgeOne: task => ApplyAcknowledge(task),
            snoozeRemainingOnX: remaining =>
            {
                foreach (var task in remaining) ApplySnooze(task);
            },
            closed: () => _reminders.ReleaseFromPopup(tasks.Select(x => x.RowId)),
            released: id => _reminders.ReleaseFromPopup([id]), // v2.3.1: per row, as soon as it leaves the popup
            stillPending: task =>
            {
                var r = FindRowById(task.RowId);
                if (r < 0) return false;
                // Drop from the popup when the task is done, deleted, or its due was edited.
                return IsStillOpenTask(r, task.Due);
            });
    }

    // Popup actions on several tasks arrive one by one; collect them and show ONE combined status message.
    private readonly List<string> _snoozedNames = [];
    private readonly List<string> _ackedNames = [];

    private static string NameList(List<string> names) =>
        names.Count == 1 ? $"\"{names[0]}\"" :
        Localizer.T("ui.names_many", names.Count, string.Join(", ", names.Take(3).Select(n => $"\"{n}\"")) + (names.Count > 3 ? " …" : ""));

    private void ApplySnooze(DueTask task)
    {
        var merged = task.Offset > 0 && DateTime.Now.AddMinutes(ReminderPlan.SnoozeMinutes) >= task.Due;
        ReminderPlan.Snooze(_session.Doc, task, DateTime.Now);
        FlushSave();
        if (merged)
        {
            StatusText.Text = Localizer.T("date.remind_at_due", task.Title, DueDates.FormatTime(task.Due));
            return;
        }
        _snoozedNames.Add(task.Title);
        if (_snoozedNames.Count == 1)
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = Localizer.T("ui.snoozed", NameList(_snoozedNames));
                _snoozedNames.Clear();
            });
    }

    private void ApplyAcknowledge(DueTask task)
    {
        ReminderPlan.Acknowledge(_session.Doc, task);
        FlushSave();
        _ackedNames.Add(task.Title);
        if (_ackedNames.Count == 1)
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = Localizer.T("ui.acknowledged", NameList(_ackedNames));
                _ackedNames.Clear();
            });
    }

    private void ApplyDone(string rowId)
    {
        var row = FindRowById(rowId);
        if (row < 0) return;
        var before = Snapshot.CloneLite(_session.Doc);
        var msg = Recurrence.CompleteAndSpawnNext(_session.Doc, row);
        _session.Doc.Acknowledged.Remove(rowId);
        _undo.Push(new SnapshotAction(Localizer.T("undo.done_next"), before, Snapshot.CloneLite(_session.Doc)));
        Rebuild();
        FlushSave();
        StatusText.Text = msg ?? Localizer.T("ui.marked_done");
        _reminders.CheckNow();
    }

    private int FindRowById(string id)
    {
        _session.Doc.EnsureRowIds();
        return _session.Doc.RowIds.FindIndex(x => x == id);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_view == ViewMode.Background) return; // v2.3.1: _busy now only blocks the F5–F8 structure keys
        if (e.Key is Key.Home or Key.End && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { OnGridKeyTunnel(sender, e); return; }
        if (HandleZoomKey(e)) return;
        if (HandleFindKey(e)) return; // 2.6.0: Ctrl+F, F3, Esc closes the find bar
        if (HandleRowMoveKey(e)) return; // v2.4.0: Alt+Shift+↑/↓ moves the selected row
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // v2.3.1 fix: text selected inside a cell → normal TextBox copy of that text (was: always the whole cell range).
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { } tb && tb.SelectionStart != tb.SelectionEnd)
                return;
            // 2.6.0: the formula bar / find box copy their own text; elsewhere the selected cells go (as Excel takes them).
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false } other && !IsDataCell(other))
                return;
            e.Handled = true;
            _ = CopySelectionAsync();
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // A TextBox being edited with its own undo history handles Ctrl+Z itself (U9).
            if (FocusedEditingBox() is { CanUndo: true }) return;
            if (FocusedAlertTextBox() is { CanUndo: true }) return;
            CommitFocusedEdit();
            e.Handled = true;
            DoUndo();
            return;
        }
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) ||
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Z))
        {
            if (FocusedEditingBox() is { CanRedo: true }) return;
            if (FocusedAlertTextBox() is { CanRedo: true }) return;
            CommitFocusedEdit();
            e.Handled = true;
            DoRedo();
            return;
        }
        if (HandleColumnMoveKeys(e)) return; // v2.4.0: Alt+Shift+←/→
        if (HandleSelectionKey(e)) { e.Handled = true; return; } // 2.6.0: arrows, Tab, F2, Delete, Ctrl+A/X/V on cells
        if (_busy && e.Key is Key.F5 or Key.F6 or Key.F7 or Key.F8) { e.Handled = true; return; }
        switch (e.Key)
        {
            case Key.F5: e.Handled = true; _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_row"), AddRowAsync)); break;
            case Key.F6: e.Handled = true; _ = RunStructureAsync(() => DeleteRowsAsync(-1)); break; // 2.6.0: every selected row
            case Key.F7: e.Handled = true; _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), AddColumnAsync)); break;
            case Key.F8: e.Handled = true; _ = RunStructureAsync(() => DeleteColumnsAsync(-1)); break; // 2.6.0: every selected column
        }
    }

    /// <summary>The focused grid TextBox whose text differs from its committed value (an edit in progress).</summary>
    private TextBox? FocusedEditingBox()
    {
        var box = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
        if (box == null || box.IsReadOnly) return null;
        return box.Tag is EditCommit c && !c.IsCommitted(box) ? box : null;
    }

    private void CommitFocusedEdit()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { Tag: EditCommit c } box)
            c.Commit(box);
    }

    /// <summary>Tracks the value a cell/header had when editing started; Commit pushes ONE undo entry.</summary>
    private sealed class EditCommit(Func<string> current, Action<string, string> push)
    {
        public string Start { get; set; } = current();
        public bool IsCommitted(TextBox box) => (box.Text ?? "") == Start;
        public void Commit(TextBox box)
        {
            var now = current();
            // Box out of sync with document (e.g. after undo/rebuild) → just resync, no entry.
            if (now != (box.Text ?? "")) { Start = now; return; }
            if (now == Start) return;
            push(Start, now);
            Start = now;
        }
    }

    private Task SnapshotStep(string label, Func<Task> action) => SnapshotStepCore(label, action);

    private async Task SnapshotStepCore(string label, Func<Task> action)
    {
        var before = Snapshot.CloneLite(_session.Doc);
        var beforeJson = Snapshot.Serialize(before);
        // Colours and reminder acks/snoozes are not in the snapshot; keep what this step removes so undo can put
        // it back (deleting a column drops its colours, deleting the due column drops every acknowledgement).
        var side = UndoSideState.Capture(_session.Doc);
        await action();
        var after = Snapshot.CloneLite(_session.Doc);
        if (Snapshot.Serialize(after) != beforeJson)
            _undo.Push(new SnapshotAction(label, before, after, side.RemovedBy(_session.Doc)));
    }

    private void DoUndo()
    {
        var label = _undo.Undo(_session.Doc);
        if (label == null) return;
        ActivityLog.Shared.Add(ActivityLevel.Info, Localizer.T("ui.undo_done", label));
        Rebuild();
        FlushSave(); // v2.4.1: undo/redo of a row move / delete is a structure change → save now, like the action itself
        StatusText.Text = Localizer.T("ui.undo_done", label);
        _reminders.CheckNow();
    }

    private void DoRedo()
    {
        var label = _undo.Redo(_session.Doc);
        if (label == null) return;
        ActivityLog.Shared.Add(ActivityLevel.Info, Localizer.T("ui.redo_done", label));
        Rebuild();
        FlushSave(); // v2.4.1: undo/redo of a row move / delete is a structure change → save now, like the action itself
        StatusText.Text = Localizer.T("ui.redo_done", label);
        _reminders.CheckNow();
    }

    private string? SyncStartup(bool enabled)
    {
        try
        {
            var r = _startup.Apply(enabled, StartupLaunch.CurrentCommand());
            return r.Ok ? null : r.Error;
        }
        catch (Exception ex) { return Localizer.T("ui.startup_failed", ex.Message); }
    }

    private async Task RunStructureAsync(Func<Task> action)
    {
        if (_busy) return;
        // v2.3.1: F5–F8 don't move the focus, so the typed text had no undo step yet; a later commit
        // then carried a stale column index.
        CommitFocusedEdit();
        try { await action(); }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("ui.action_failed", ex.Message);
            ActivityLog.Shared.Add(ActivityLevel.Error, StatusText.Text);
            AppLog.Write("ui", "action failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private Task AddRowAsync()
    {
        var err = _session.AddRow();
        if (err != null) { StatusText.Text = err; return Task.CompletedTask; }
        Rebuild(); FocusActiveCell(); FlushSave();
        return Task.CompletedTask;
    }

    private Task AddColumnAsync()
    {
        var err = _session.AddColumn();
        if (err != null) { StatusText.Text = err; return Task.CompletedTask; }
        Rebuild(); FocusActiveCell(); FlushSave();
        return Task.CompletedTask;
    }

    private async Task DeleteRowAsync()
    {
        _busy = true;
        try
        {
            RowDeleteAction? undoDel = null;
            // Capture undo AFTER the confirm dialog, from the row actually removed (re-found by row id),
            // so inserts/deletes/edits made while the dialog was open can't desync the undo step.
            var r = await _session.DeleteRowAsync(_confirm.ConfirmAsync,
                at => undoDel = RowDeleteAction.Capture(_session.Doc, at));
            if (!r.Changed) { if (!string.IsNullOrEmpty(r.Status)) StatusText.Text = r.Status; }
            else
            {
                if (undoDel != null) _undo.Push(undoDel);
                Rebuild(); FlushSave();
            }
        }
        finally { _busy = false; }
    }

    private async Task DeleteColumnAsync()
    {
        _busy = true;
        try
        {
            var r = await _session.DeleteColumnAsync(_confirm.ConfirmAsync);
            if (!r.Changed)
            {
                if (!string.IsNullOrEmpty(r.Status)) StatusText.Text = r.Status;
                if (r.Status == DeletePlanner.DueColumnProtected) ShowNotice(r.Status);
            }
            else
            {
                Rebuild(); FlushSave();
                AfterColumnRemoved();
            }
        }
        finally { _busy = false; }
    }

    private void SortByDue()
    {
        var dueCol = _session.Doc.DueColumnIndex();
        if (dueCol < 0) return;
        var indexed = _session.Doc.Rows
            .Select((row, i) => (row, id: _session.Doc.RowIdAt(i), i))
            .OrderBy(x => DueDates.TryParse(x.row.ElementAtOrDefault(dueCol), out var d) ? d : DateTime.MaxValue)
            .ThenBy(x => x.i)
            .ToList();
        _session.Doc.Rows = indexed.Select(x => x.row).ToList();
        _session.Doc.RowIds = indexed.Select(x => x.id).ToList();
        Rebuild();
        FlushSave();
        StatusText.Text = Localizer.T("ui.sorted_by_due");
    }

    private void OnMuteSoundChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_tracking || e.Property != ToggleButton.IsCheckedProperty) return;
        _session.Doc.MuteSound = MuteSoundBox.IsChecked == true;
        NotificationSound.Muted = _session.Doc.MuteSound;
        ScheduleSave();
        StatusText.Text = Localizer.T(_session.Doc.MuteSound ? "ui.sound_off" : "ui.sound_on");
    }

    private void OnHideDoneChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_tracking || e.Property != ToggleButton.IsCheckedProperty) return;
        _session.Doc.HideDone = HideDoneBox.IsChecked == true;
        Rebuild();
        ScheduleSave();
    }

    private void OnTopmostBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_tracking || e.Property != ToggleButton.IsCheckedProperty) return;
        _session.Doc.Window.Layer = TopmostBox.IsChecked == true ? DesktopLayerKind.Floating : DesktopLayerKind.Desktop;
        ApplyRequestedLayer();
        ScheduleSave();
    }

    private void OnLockBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_tracking || e.Property != ToggleButton.IsCheckedProperty) return;
        _session.Doc.Window.Locked = LockBox.IsChecked == true;
        CanResize = !_session.Doc.Window.Locked;
        if (_session.Doc.Window.Locked)
        {
            _session.Doc.Window.X = Position.X;
            _session.Doc.Window.Y = Position.Y;
        }
        ScheduleSave();
        UpdateStatus();
    }

    private void OnLaunchBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_tracking || e.Property != ToggleButton.IsCheckedProperty) return;
        var want = LaunchBox.IsChecked == true;
        var err = SyncStartup(want);
        if (err != null)
        {
            _tracking = false;
            LaunchBox.IsChecked = _session.Doc.LaunchAtLogin;
            _tracking = true;
            StatusText.Text = err;
            return;
        }
        _session.Doc.LaunchAtLogin = want;
        ScheduleSave();
        StatusText.Text = Localizer.T(want ? "ui.launch_on" : "ui.launch_off");
    }

    private void InstallShellHook()
    {
        if (!OperatingSystem.IsWindows()) return;
        var taskbarCreated = WindowsTaskbar.TaskbarCreatedMessage();
        Win32Properties.AddWndProcHookCallback(this, (nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg == taskbarCreated) OnExplorerRestart();
            else if (msg == 126) OnDisplayChanged();
            else if (msg == 786 && wParam == 17732) ShowEditor();
            return 0;
        });
    }

    private void OnExplorerRestart()
    {
        AppLog.Write("desktop", $"TaskbarCreated (Explorer restarted), view = {_view}");
        if (_view == ViewMode.Edit) ApplyTaskbarPresence(); // v2.4.2: new taskbar → re-add the editor's button
        if (_view == ViewMode.Background)
        {
            // The wallpaper survives an Explorer restart; the tray icon is the way back, so it must come back too.
            _trayReady = TryCreateTray();
            if (RecoveryGate.AfterShellRestart(_view, CurrentRoutes()) == ViewMode.Edit)
                EnterEdit(Localizer.T("ui.tray_lost"));
        }
    }

    private void OnDisplayChanged()
    {
        EnsureVisible();
        // Another resolution, scale or set of monitors: draw the wallpaper for the new layout.
        if (_view == ViewMode.Background) ScheduleWallpaperRefresh();
    }

    /// <summary>The editor's layer: "floating" keeps it above other windows; otherwise it is a normal window.
    /// (The desktop layer used to attach the window to the desktop; wallpaper mode replaced that in 2.6.0.)</summary>
    private void ApplyRequestedLayer()
    {
        var floating = _session.Doc.Window.Layer == DesktopLayerKind.Floating;
        Topmost = floating;
        _session.Doc.Window.Topmost = floating;
        UpdateStatus();
    }

    private void ShowNotice(string? text)
    {
        ModeNotice.Text = text ?? "";
        ModeNotice.IsVisible = !string.IsNullOrEmpty(text);
    }

    private nint WindowHandle() => TryGetPlatformHandle()?.Handle ?? 0;

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!_tracking || _applyingPlacement || _view != ViewMode.Edit) return;
        if (_session.Doc.Window.Locked && _session.Doc.Window.X is int lx && _session.Doc.Window.Y is int ly)
        {
            if (e.Point.X != lx || e.Point.Y != ly)
            {
                _applyingPlacement = true;
                Position = new PixelPoint(lx, ly);
                _applyingPlacement = false;
            }
            return;
        }
        _session.Doc.Window.X = e.Point.X;
        _session.Doc.Window.Y = e.Point.Y;
        ScheduleSave();
    }

    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (!_tracking) return;
        if (e.ClientSize.Width > 1) _session.Doc.Window.Width = (int)Math.Round(e.ClientSize.Width);
        if (e.ClientSize.Height > 1) _session.Doc.Window.Height = (int)Math.Round(e.ClientSize.Height);
        ScheduleSave();
        FitColumns();
    }

    private void ClampIfPlaced() => EnsureVisible();

    private void ScheduleSave()
    {
        if (!_tracking) return;
        CaptureHistory(); // 2.6.0: before anything redraws, so a row set to done stops counting at once
        StatusText.Text = Localizer.T("ui.saving");
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshActivityBar(); // 2.6.0: "edited — saving shortly"
    }

    private bool FlushSave()
    {
        if (!_tracking) return true;
        _saveTimer.Stop();
        return DoSave();
    }

    private bool DoSave()
    {
        CaptureHistory(); // 2.6.0: paths that save without ScheduleSave (structure changes, undo, quit)
        if (_preserveExistingFile)
        {
            // v2.4.1: note.json stays untouched, but edits of this session go to note.unsaved.json
            // (before: nothing was written and quitting reported success → the whole session was lost).
            // PreserveKey also covers alert boxes and colours, which the undo snapshot leaves out.
            if (PreserveKey(_session.Doc) == _preserveBaseline)
            {
                StatusText.Text = _loadWarning ?? TableLimits.FileTooBigKeptMessage;
                return true;
            }
            try
            {
                var side = NoteStore.SaveSideCopy(_path, _session.Doc);
                _sideCopyName = Path.GetFileName(side);
                _saveFailureMessage = null;
                ActivityLog.Shared.Add(ActivityLevel.Warning, Localizer.T("activity.saved_side", Path.GetFileName(side)), "save");
                ScheduleWallpaperRefresh();
                StatusText.Text = Localizer.T("ui.saved_side", _loadWarning ?? TableLimits.FileTooBigKeptMessage, Path.GetFileName(side));
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("save", "note.unsaved.json failed: " + ex.Message);
                _saveFailureMessage = SaveFeedback.Failure(ex);
                StatusText.Text = _saveFailureMessage;
                ActivityLog.Shared.Add(ActivityLevel.Error, _saveFailureMessage, "save-failed");
                return false; // quit is blocked with the "not saved" dialog instead of losing the edits
            }
        }
        try
        {
            CaptureBounds();
            long size;
            var colsBefore = _session.Doc.Columns.Count;
            var rowsBefore = _session.Doc.Rows.Count;
            using (UiActivity.Scope("save note.json"))
                size = NoteStore.Save(_path, _session.Doc);
            // v2.3.1 fix: Save() → Normalize() can re-add a deleted Status column (or trim rows/columns);
            // the grid kept the old layout, so the new column only appeared after some later rebuild.
            if (_session.Doc.Columns.Count != colsBefore || _session.Doc.Rows.Count != rowsBefore)
                Dispatcher.UIThread.Post(Rebuild);
            _saveFailureMessage = null;
            ActivityLog.Shared.Add(ActivityLevel.Done, Localizer.T("activity.saved", Path.GetFileName(_path)), "save"); // 2.6.0
            UpdateStatus();
            ScheduleWallpaperRefresh(); // wallpaper mode: a reminder marked done etc. shows on the desktop too
            if (size > TableLimits.MaxSaveFileBytes * 3L / 4) // P5: warn from 75 % of the cap
                StatusText.Text = Localizer.T("save.near_limit", (size / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        catch (NoteStore.TooLargeException ex)
        {
            _saveFailureMessage = ex.Message;
            StatusText.Text = ex.Message;
            ActivityLog.Shared.Add(ActivityLevel.Error, ex.Message, "save-failed");
            return false;
        }
        catch (Exception ex)
        {
            _saveFailureMessage = SaveFeedback.Failure(ex);
            StatusText.Text = _saveFailureMessage;
            ActivityLog.Shared.Add(ActivityLevel.Error, _saveFailureMessage, "save-failed");
            AppLog.Write("save", "note.json save failed: " + ex.GetType().Name + ": " + ex.Message); // v2.4.1: was only in the status bar (lost on OS shutdown)
            return false;
        }
    }

    private void CaptureBounds()
    {
        if (Width > 1) _session.Doc.Window.Width = (int)Math.Round(Width);
        if (Height > 1) _session.Doc.Window.Height = (int)Math.Round(Height);
        if (_tracking && _view == ViewMode.Edit) // hidden in wallpaper mode: keep the last position the user saw
        {
            _session.Doc.Window.X = Position.X;
            _session.Doc.Window.Y = Position.Y;
        }
        _session.Doc.Window.Topmost = Topmost;
    }

    // ---------------- language / zoom (settings.json only — note.json is never written by these) ----------------
    private readonly AppSettings _settings;

    private void SwitchLanguage(string code)
    {
        if (!Localizer.SetLanguage(code)) return;
        _settings.Language = Localizer.Current;
        _settings.Save(_path);
    }

    private void ApplyLanguage()
    {
        // Keep a cell being typed in: commit it first (normal edit), then re-render labels.
        CommitFocusedEdit();
        ApplyLanguageTexts();
        Rebuild();
        if (_trayReady) _trayReady = TryCreateTray();
        UpdateStatus();
        StatusText.Text = Localizer.T("status.language");
        ScheduleWallpaperRefresh(); // status names and "+ N more rows" on the wallpaper
    }

    private void ApplyLanguageTexts()
    {
        string T(string k) => Localizer.T(k);
        Title = T("app.title");
        ApplyFormulaBarTexts(); // 2.6.0
        ApplyFindTexts();
        ApplyActivityTexts();
        AddRowButton.Content = T("toolbar.add_row"); ToolTip.SetTip(AddRowButton, T("tip.add_row"));
        DeleteRowButton.Content = T("toolbar.delete_row"); ToolTip.SetTip(DeleteRowButton, T("tip.delete_row"));
        AddColumnButton.Content = T("toolbar.add_column"); ToolTip.SetTip(AddColumnButton, T("tip.add_column"));
        ExportButton.Content = T("toolbar.export"); ToolTip.SetTip(ExportButton, T("tip.export"));
        HideDoneBox.Content = T("toolbar.hide_done");
        SortDueButton.Content = T("toolbar.sort_due"); ToolTip.SetTip(SortDueButton, T("tip.sort_due"));
        ToolTip.SetTip(ZoomOutButton, T("tip.zoom_out"));
        ToolTip.SetTip(ZoomResetButton, T("tip.zoom_reset"));
        ToolTip.SetTip(ZoomInButton, T("tip.zoom_in"));
        ApplyWallpaperTexts();
        ApplySketchTexts();
        RenderAlertBoxes();
        ApplyColorButtonText();
        ApplyMenuBarTexts();
    }

    private void SetZoom(int scale)
    {
        if (_view == ViewMode.Background) return; // Z4
        var target = Math.Clamp(scale, AppSettings.MinScale, AppSettings.MaxScale);
        var changed = target != _settings.Scale;
        _settings.Scale = target;
        ApplyZoom();
        if (changed) _settings.Save(_path);
        StatusText.Text = target >= AppSettings.MaxScale ? Localizer.T("status.zoom_max", target)
            : target <= AppSettings.MinScale ? Localizer.T("status.zoom_min", target)
            : Localizer.T("status.zoom", target);
    }

    private void ApplyZoom()
    {
        var f = _settings.Scale / 100.0;
        ZoomHost.LayoutTransform = Math.Abs(f - 1) < 0.001 ? null : new ScaleTransform(f, f);
        ZoomResetButton.Content = _settings.Scale + "%";
        ReminderPopup.Zoom = f;
    }

    private void OnZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        e.Handled = true; // never scroll the table while Ctrl is held (Z2)
        if (_view == ViewMode.Background || e.Delta.Y == 0) return;
        SetZoom(_settings.Scale + Math.Sign(e.Delta.Y) * AppSettings.ScaleStep);
    }

    /// <returns>true when the key was a zoom shortcut (Z1, Z15: Ctrl without Alt).</returns>
    private bool HandleZoomKey(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false;
        int? next = e.Key switch
        {
            Key.OemPlus or Key.Add => _settings.Scale + AppSettings.ScaleStep,
            Key.OemMinus or Key.Subtract => _settings.Scale - AppSettings.ScaleStep,
            Key.D0 or Key.NumPad0 => 100,
            _ => null
        };
        if (next is not int n) return false;
        e.Handled = true; // nothing is typed into the cell
        SetZoom(n);
        return true;
    }

    // ---------------- export (E1–E10) ----------------
    private bool _exporting;

    private async Task ExportAsync()
    {
        if (_view != ViewMode.Edit || _exporting) return;
        CommitFocusedEdit();
        var includeHidden = true;
        if (_session.Doc.HideDone)
        {
            var choice = await AskIncludeHidden();
            if (choice == null) return;
            includeHidden = choice.Value;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { CanSave: true } sp) { StatusText.Text = Localizer.T("export.failed", "no save dialog"); return; }
        var file = await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Localizer.T("export.title"),
            SuggestedFileName = Localizer.T("export.default_name", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            DefaultExtension = "xlsx",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType(Localizer.T("export.xlsx")) { Patterns = ["*.xlsx"] },
                new Avalonia.Platform.Storage.FilePickerFileType(Localizer.T("export.csv")) { Patterns = ["*.csv"] },
                new Avalonia.Platform.Storage.FilePickerFileType(Localizer.T("export.txt")) { Patterns = ["*.txt"] },
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return; // cancelled → no file, note.json untouched
        var (headers, rows) = Exporter.FromDocument(_session.Doc, respectHideDone: !includeHidden);
        var cols = new ExportColumns(_session.Doc.StatusColumnIndex(), _session.Doc.DueColumnIndex());
        var format = Exporter.FormatForPath(path);
        _exporting = true;
        ExportButton.IsEnabled = false;
        StatusText.Text = Localizer.T("export.running");
        var run = ActivityLog.Shared.Begin(Localizer.T("activity.exporting", Path.GetFileName(path))); // 2.6.0: the bar moves
        try
        {
            await Task.Run(() => Exporter.Export(rows, headers, path, format, cols)); // off the UI thread
            StatusText.Text = Localizer.T(rows.Count == 1 ? "export.done_one" : "export.done", rows.Count, Path.GetFileName(path));
            run.Done(StatusText.Text);
            AppLog.Write("export", $"{format} {rows.Count} rows → {path}");
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("export.failed", ex.Message);
            run.Fail(StatusText.Text);
            AppLog.Write("export", "failed: " + ex.Message);
        }
        finally
        {
            _exporting = false;
            ExportButton.IsEnabled = true;
        }
    }

    /// <returns>true = include hidden done rows, false = visible only, null = cancelled.</returns>
    private async Task<bool?> AskIncludeHidden()
    {
        var box = new CheckBox { Content = Localizer.T("export.include_hidden"), IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = Localizer.T("export.continue"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Localizer.T("common.cancel"), IsCancel = true };
        var dlg = new Window
        {
            Title = Localizer.T("export.title"), Width = 380, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = AppFonts.Thai,
            Content = new LayoutTransformControl
            {
                LayoutTransform = _settings.Scale == 100 ? null : new ScaleTransform(_settings.Scale / 100.0, _settings.Scale / 100.0),
                Child = new StackPanel
                {
                    Margin = new Thickness(14), Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = Localizer.T("export.hidden_info"), TextWrapping = TextWrapping.Wrap },
                        box,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel } }
                    }
                }
            }
        };
        ok.Click += (_, _) => dlg.Close(box.IsChecked == true);
        cancel.Click += (_, _) => dlg.Close(null);
        return await dlg.ShowDialog<bool?>(this);
    }

    public const double RowLabelWidth = 44;
    private List<int> _visibleRows = [];

    /// <summary>
    /// Header row is a normal Grid; data rows are an ItemsControl on a VirtualizingStackPanel, so only the rows
    /// on screen are built (v2.2.1 built every cell of every row — 200 rows ≈ 1 GB RAM, 11 s of CPU).
    /// </summary>
    private void Rebuild()
    {
        // v2.3.1: the cell being typed in is about to be destroyed → keep its undo step first.
        if (!_ignoreText) CommitFocusedEdit();
        using var _uiScope = UiActivity.Scope("rebuild table");
        _headerBoxes.Clear();
        ClearMenuHeaderBoxes();
        TableGrid.Children.Clear();
        TableGrid.RowDefinitions.Clear();
        TableGrid.ColumnDefinitions.Clear();
        ApplyColumnDefinitions(TableGrid);
        TableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var corner = new Border
        {
            Background = RowNumBg, BorderBrush = GridLine, BorderThickness = new Thickness(1),
            MinHeight = 32, Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(corner, Localizer.T("cell.select_all_tip"));
        corner.PointerPressed += (_, e) => { e.Handled = true; SelectAll(); }; // 2.6.0: like the corner in Excel
        TableGrid.Children.Add(corner);
        for (var k = 0; k < _session.Doc.Columns.Count; k++)
        {
            var control = MakeHeader(k);
            Grid.SetRow(control, 0);
            Grid.SetColumn(control, k + 1);
            TableGrid.Children.Add(control);
        }

        var visibleRows = new List<int>();
        var sc = _session.Doc.StatusColumnIndex();
        for (var r = 0; r < _session.Doc.Rows.Count; r++)
        {
            if (_session.Doc.HideDone && sc >= 0 && TaskStatuses.IsDone(_session.Doc.GetCell(r, sc))) continue;
            visibleRows.Add(r);
        }
        _visibleRows = visibleRows;

        EmptyText.IsVisible = visibleRows.Count == 0;
        EmptyText.Text = Localizer.T(_session.Doc.Rows.Count == 0 ? "table.empty" : "table.all_hidden");
        EmptyText.FontFamily = AppFonts.Thai;

        if (RowsList.ItemsPanelRoot is not VirtualizingStackPanel)
            RowsList.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        RowsList.ItemTemplate ??= new FuncDataTemplate<int>((rowIndex, _) => BuildRow(rowIndex), supportsRecycling: false);
        // New list instance = re-realize only the rows currently on screen (cheap), scroll offset is kept.
        RowsList.ItemsSource = visibleRows.ToList();

        if (FindOpen) RefreshFindMatches(); // 2.6.0: marks follow the changed table (the selection stays)
        Highlight();
        FitColumns();
    }

    private void ApplyColumnDefinitions(Grid grid)
    {
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(RowLabelWidth)));
        for (var i = 0; i < _session.Doc.Columns.Count; i++)
            grid.ColumnDefinitions.Add(GroupColumnDefinition(i));
    }

    /// <summary>Column resize: header + the few realized rows (others pick the width up when realized).</summary>
    private void ApplyColumnWidth(int column)
    {
        var w = GroupColumnLength(column);
        if (column + 1 < TableGrid.ColumnDefinitions.Count) TableGrid.ColumnDefinitions[column + 1].Width = w;
        foreach (var c in RowsList.GetRealizedContainers())
            if (c is ContentPresenter { Child: Grid g } && column + 1 < g.ColumnDefinitions.Count)
                g.ColumnDefinitions[column + 1].Width = w;
    }

    private Control BuildRow(int rowIndex)
    {
        var grid = new Grid { Tag = rowIndex };
        PopulateRow(grid, rowIndex);
        return grid;
    }

    /// <summary>
    /// v2.3.1: re-create ONE realized row in place (status / due / repeat / reminder change). Before, every such
    /// change called Rebuild(): header + all visible rows were re-created, and the cell the user had just clicked
    /// was destroyed → focus lost ("can't type"), plus a visible stutter. Rows not on screen are built on realize.
    /// </summary>
    private void RefreshRow(int rowIndex)
    {
        using var _uiScope = UiActivity.Scope("refresh row");
        foreach (var c in RowsList.GetRealizedContainers())
        {
            if (c is not ContentPresenter { Child: Grid g } || g.Tag is not int r || r != rowIndex) continue;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            // Which column of this row holds the focus (direct child of the row grid), if any.
            var focusCol = -1;
            for (var v = focused; v != null; v = v.GetVisualParent())
                if (v.GetVisualParent() is Grid pg && ReferenceEquals(pg, g)) { focusCol = Grid.GetColumn((Control)v) - 1; break; }
            if (focusCol >= 0) CommitFocusedEdit(); // keep the undo entry of a text being typed in this row
            _ignoreText = true; // old controls may raise LostFocus/TextChanged while removed
            try { PopulateRow(g, rowIndex); }
            finally { _ignoreText = false; }
            if (focusCol >= 0) FocusCell(rowIndex, focusCol);
            return;
        }
    }

    /// <summary>Row change that may hide/show rows (Hide done) or add rows needs the full rebuild; otherwise one row.</summary>
    private void RefreshRowOrRebuild(int rowIndex, bool structural)
    {
        if (structural || _session.Doc.HideDone) Rebuild();
        else RefreshRow(rowIndex);
    }

    private void PopulateRow(Grid grid, int rowIndex)
    {
        grid.Children.Clear();
        ApplyColumnDefinitions(grid);
        if (rowIndex < 0 || rowIndex >= _session.Doc.Rows.Count) return;
        var rowBg = RowBackground(rowIndex);
        var rowLabel = RowLabelColors(rowIndex);
        var number = new TextBlock
        {
            Text = (rowIndex + 1).ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = rowLabel.Foreground, FontFamily = AppFonts.Thai, Margin = new Thickness(4, 0)
        };
        var border = new Border
        {
            Background = rowLabel.Background,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            Child = _view == ViewMode.Edit ? WithRowGrip(number) : number // 2.6.0: ⋮⋮ moves the row, the number selects it
        };
        border.PointerPressed += (_, e) =>
        {
            if (_view == ViewMode.Edit) { OnRowLabelPressed(rowIndex, border, e); return; } // 2.6.0: whole rows, like Excel
            _session.ChooseRow(rowIndex);
            Highlight();
            UpdateStatus();
            e.Handled = true;
        };
        AttachRowMove(border, rowIndex); // v2.4.0: drag / right-click menu to move the row
        grid.Children.Add(border);
        for (var c = 0; c < _session.Doc.Columns.Count; c++)
        {
            var cell = GroupCell(rowIndex, c, rowBg.cell);
            Grid.SetColumn(cell, c + 1);
            grid.Children.Add(cell);
        }
        ApplySavedRowHeight(grid, rowIndex); // v2.6: a set height clips; auto rows are unchanged
        ApplyOverlays(grid, rowIndex); // 2.6.0: selection and find marks
    }

    private (IBrush label, IBrush cell) RowBackground(int row) => WithRowStyle(row, StatusRowBackground(row)); // v2.4.0

    private (IBrush label, IBrush cell) StatusRowBackground(int row)
    {
        var statusCol = _session.Doc.StatusColumnIndex();
        var dueCol = _session.Doc.DueColumnIndex();
        var status = statusCol >= 0 ? _session.Doc.GetCell(row, statusCol) : "";
        if (TaskStatuses.IsDone(status)) return (DoneBg, DoneBg);

        if (dueCol >= 0 && DueDates.TryParse(_session.Doc.GetCell(row, dueCol), out var due))
        {
            var now = DateTime.Now;
            if (DueDates.IsOverdue(due, now)) return (OverdueBg, OverdueBg);
            if (DueDates.IsDueToday(due, now)) return (TodayBg, TodayBg);
        }
        return (RowNumBg, CellBg);
    }

    private Control MakeHeader(int column)
    {
        if (TryHiddenHeader(column, out var hiddenHeader)) return hiddenHeader;
        var box = MakeTextBox(_session.Doc.Columns[column], header: true, -1, column, HeaderBg);
        PaintColumnHeader(box, column);
        _headerBoxes.Add(box);
        RememberMenuHeaderBox(column, box);
        var thumb = new Border { Width = 6, Background = GridLine, Cursor = ResizeCursor };
        Point? origin = null;
        var startWidth = 0;
        thumb.PointerPressed += (_, e) =>
        {
            origin = e.GetPosition(thumb);
            startWidth = _session.Doc.ColumnWidth(column);
            e.Pointer.Capture(thumb);
            e.Handled = true;
        };
        thumb.PointerMoved += (_, e) =>
        {
            if (origin is not { } o) return;
            var dx = e.GetPosition(thumb).X - o.X;
            _session.Doc.SetColumnWidth(column, startWidth + (int)dx);
            ApplyColumnWidth(column);
            ScheduleSave();
            e.Handled = true;
        };
        thumb.PointerReleased += (_, e) => { origin = null; e.Pointer.Capture(null); };

        var grid = new Grid();
        var slot = AddGroupToggle(grid, column);
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(box, slot);
        Grid.SetColumn(thumb, slot + 1);
        grid.Children.Add(box);
        grid.Children.Add(thumb);
        return WithColumnGrip(column, box, grid); // v2.4.0: drag grip + column menu
    }

    private Control MakeCell(int row, int column, IBrush bg)
    {
        // Prefer stable roles over header names
        var role = row >= 0 ? (_session.Doc.ColumnRolesList.ElementAtOrDefault(column) ?? "") : "";
        Control control;
        if (role == ColumnRoles.Status || column == _session.Doc.StatusColumnIndex())
            control = MakeStatusCombo(row, column, bg);
        else if (role == ColumnRoles.Due || column == _session.Doc.DueColumnIndex())
            control = MakeDueEditor(row, column, bg);
        else
            control = MakeTypedCell(row, column, bg) ?? // v2.4.0: number / choice / check columns
                      MakeTextBox(_session.Doc.GetCell(row, column), false, row, column, bg);

        PaintDataCell(control, row, column);
        if (control is not TextBox && control.Tag as string != "days") AttachTypedCell(control, row, column); // 2.6.0

        var statusCol = _session.Doc.StatusColumnIndex();
        if (statusCol >= 0 && TaskStatuses.IsDone(_session.Doc.GetCell(row, statusCol)))
            control = WithStrikethrough(control);
        return AttachDetailButton(row, column, control);
    }

    private static Control WithStrikethrough(Control inner)
    {
        var grid = new Grid();
        grid.Children.Add(inner);
        var line = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Avalonia.Point(4, 16),
            EndPoint = new Avalonia.Point(2000, 16),
            Stroke = StrikeFg,
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
            Opacity = 0.85
        };
        grid.Children.Add(line);
        return grid;
    }

    private Control MakeStatusCombo(int row, int column, IBrush bg)
    {
        // Blank stays blank in the file (never auto-filled); it only DISPLAYS as "Not started".
        var raw = _session.Doc.GetCell(row, column);
        var current = TaskStatuses.DisplayCode(raw);
        var labels = StatusLabels();

        var combo = new ComboBox
        {
            ItemsSource = labels,
            SelectedIndex = Array.IndexOf(TaskStatuses.Codes, current),
            FontFamily = AppFonts.Thai,
            FontSize = 14,
            Background = bg,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = _view == ViewMode.Edit,
            IsHitTestVisible = _view == ViewMode.Edit
        };
        if (TaskStatuses.IsDone(current))
        {
            combo.Foreground = DoneFg;
            // strike via opacity; Avalonia TextBlock decorations limited on ComboBox
            combo.Opacity = 0.75;
        }
        combo.PointerPressed += (_, e) =>
        {
            if (_view == ViewMode.Background) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _session.ExtendTo(row, column);
            else _session.ChooseCell(row, column);
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_ignoreText || combo.SelectedIndex < 0) return;
            var s = TaskStatuses.Codes[combo.SelectedIndex];
            if (s == current) return; // e.g. blank shown as todo → no write
            var prev = _session.Doc.GetCell(row, column);
            if (TaskStatuses.IsDone(s) && !TaskStatuses.IsDone(prev))
            {
                // Whole-document snapshot only here (recurrence may add a row); plain changes use CellEditAction.
                var before = Snapshot.CloneLite(_session.Doc);
                var rowsBefore = _session.Doc.Rows.Count;
                var msg = Recurrence.CompleteAndSpawnNext(_session.Doc, row);
                _undo.Push(new SnapshotAction(Localizer.T("undo.change_status"), before, Snapshot.CloneLite(_session.Doc)));
                ScheduleSave();
                var structural = _session.Doc.Rows.Count != rowsBefore;
                Dispatcher.UIThread.Post(() => RefreshRowOrRebuild(row, structural)); // after the drop-down closes
                if (msg != null) StatusText.Text = msg;
                _reminders.CheckNow();
                return;
            }
            if (!_session.SetCell(row, column, s)) return;
            _undo.Push(CellEditAction.For(_session.Doc, _session.Doc.RowIdAt(row), column, prev, s, Localizer.T("undo.change_status")));
            // Re-opened: recalc from now, don't pop missed advance reminders (on-time still can).
            if (TaskStatuses.IsDone(prev) && !TaskStatuses.IsDone(s))
                ReminderPlan.Reset(_session.Doc, _session.Doc.RowIdAt(row), DateTime.Now);
            ScheduleSave();
            Dispatcher.UIThread.Post(() => RefreshRowOrRebuild(row, structural: false));
            _reminders.CheckNow();
        };
        AttachEnterNavigation(combo, row, column, skip: () => combo.IsDropDownOpen); // open list: Enter picks the item
        return combo;
    }

    // Time-of-day only (0:00–23:59). "h" in TimeSpan formats is hours 0–23; no day component is accepted.
    private static readonly string[] TimeOfDayFormats = [@"h\:mm", @"hh\:mm"];

    private Control MakeDueEditor(int row, int column, IBrush bg)
    {
        var id = _session.Doc.RowIdAt(row);
        var current = _session.Doc.GetCell(row, column);
        var hasDate = DueDates.TryParse(current, out var parsed);

        var datePicker = new CalendarDatePicker
        {
            SelectedDate = hasDate ? parsed.Date : null,
            Width = 140,
            // Thai users read day/month/year (the default en-US culture showed MM/dd)
            SelectedDateFormat = CalendarDatePickerFormat.Custom,
            CustomDateFormatString = "dd/MM/yyyy",
            FontFamily = AppFonts.Thai,
            IsEnabled = _view == ViewMode.Edit,
            PlaceholderText = Localizer.T("date.placeholder")
        };
        var timeBox = new TextBox
        {
            // No due (or date-only due) → leave empty so the "HH:mm" placeholder shows; never invent 09:00.
            Text = hasDate && DueDates.HasTime(current) ? DueDates.FormatTime(parsed) : "",
            Width = 76, // 58 clipped "14:30" with the Thai font
            FontFamily = AppFonts.Thai,
            IsEnabled = _view == ViewMode.Edit,
            PlaceholderText = "HH:mm",
            Margin = new Thickness(4, 0, 0, 0)
        };
        var recur = new ComboBox
        {
            ItemsSource = Recurrence.Labels,
            SelectedIndex = Recurrence.IndexOf(_session.Doc.Recurrence.TryGetValue(id, out var rv) ? rv : null),
            Width = 100,
            FontFamily = AppFonts.Thai,
            FontSize = 12,
            IsEnabled = _view == ViewMode.Edit,
            Margin = new Thickness(4, 0, 0, 0)
        };
        var pick = new Button
        {
            Content = "📅",
            Classes = { "tool" },
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = _view == ViewMode.Edit
        };
        ToolTip.SetTip(pick, Localizer.T("date.pick_tip"));

        bool CommitDue(DateTime dt, bool fromUser, bool dateOnly = false)
        {
            var before = _session.Doc.GetCell(row, column);
            // Keep date-only dues date-only when the time stays 00:00 (R3), or when no time was entered.
            var text = dateOnly || (!string.IsNullOrWhiteSpace(before) && !DueDates.HasTime(before) && dt.TimeOfDay == TimeSpan.Zero)
                ? DueDates.FormatDate(dt)
                : DueDates.Format(dt);
            // No change (e.g. just tabbing through the time box) → no undo step, no ack/snooze reset.
            if (before == text) return false;
            // Same due moment and same date-only/with-time form (e.g. "5/1/2026 9:00" vs "2026-01-05 09:00")
            // → don't rewrite the cell or reset snooze/ack just because the text would be reformatted.
            if (DueDates.TryParse(before, out var prevDue) && prevDue == dt &&
                (!fromUser || DueDates.HasTime(before) == DueDates.HasTime(text))) return false;
            _undo.Push(CellEditAction.For(_session.Doc, id, column, before, text, Localizer.T("date.undo_set")));
            _session.SetCell(row, column, text);
            // Editing due clears snooze + acknowledgement for this due
            ReminderPlan.Reset(_session.Doc, id, DateTime.Now);
            // Keep monthly anchor in sync
            if (_session.Doc.Recurrence.TryGetValue(id, out var kind) &&
                Recurrence.NormalizeKind(kind).StartsWith(Recurrence.MonthlyPrefix, StringComparison.Ordinal))
                _session.Doc.Recurrence[id] = Recurrence.MonthlyPrefix + dt.Day;
            ScheduleSave();
            _reminders.CheckNow();
            return true;
        }

        bool SyncFromControls()
        {
            var pickedDate = datePicker.SelectedDate?.Date;
            var timeText = timeBox.Text?.Trim() ?? "";
            if (timeText.Length == 0)
            {
                // No date and no time → nothing to commit (Tab through an empty cell must not write today 09:00).
                if (pickedDate == null) return false;
                return CommitDue(pickedDate.Value, true, dateOnly: true); // date only → stays date-only
            }
            // Clock time only ("9:30", "09:30"); bare numbers like "9" are NOT day counts and are rejected.
            if (!TimeSpan.TryParseExact(timeText, TimeOfDayFormats, System.Globalization.CultureInfo.InvariantCulture, out var ts))
            {
                var cur = _session.Doc.GetCell(row, column);
                timeBox.Text = DueDates.TryParse(cur, out var curDue) && DueDates.HasTime(cur) ? DueDates.FormatTime(curDue) : "";
                StatusText.Text = Localizer.T("date.bad_time", timeText);
                return false; // keep the previous cell value; no silent 09:00
            }
            var date = pickedDate ?? (DueDates.TryParse(_session.Doc.GetCell(row, column), out var curDate) ? curDate.Date : DateTime.Today);
            return CommitDue(date.Add(ts), true);
        }

        datePicker.SelectedDateChanged += (_, _) =>
        {
            if (_ignoreText || datePicker.SelectedDate == null) return;
            // v2.3.1: refresh only this row (colours), after the calendar pop-up has closed.
            if (SyncFromControls()) Dispatcher.UIThread.Post(() => RefreshRow(row));
        };
        // v2.3.1 fix: LostFocus fires while focus moves to the NEXT cell; a full Rebuild() here destroyed that cell,
        // so the click/Tab into it was lost and the user could not type. Now only this row is refreshed, later.
        timeBox.LostFocus += (_, _) => { if (!_ignoreText && SyncFromControls()) Dispatcher.UIThread.Post(() => RefreshRow(row)); };
        recur.SelectionChanged += (_, _) =>
        {
            if (recur.SelectedIndex < 0) return;
            var due = datePicker.SelectedDate?.Date ?? (hasDate ? parsed.Date : DateTime.Today);
            var oldValue = _session.Doc.Recurrence.TryGetValue(id, out var ok0) ? ok0 : null;
            if (Recurrence.IndexOf(oldValue) == recur.SelectedIndex) return;
            var newKind = Recurrence.FromIndex(recur.SelectedIndex, due);
            var before = Snapshot.CloneLite(_session.Doc);
            if (newKind == Recurrence.None) _session.Doc.Recurrence.Remove(id);
            else _session.Doc.Recurrence[id] = newKind;
            _undo.Push(new SnapshotAction(Localizer.T("undo.set_repeat"), before, Snapshot.CloneLite(_session.Doc)));
            ScheduleSave();
            Dispatcher.UIThread.Post(() => RefreshRow(row)); // 🔁 badge refresh right away (this row only)
        };

        // Button handles the left click itself (PointerPressed never reaches us), so use Click and
        // record Shift from a tunnel PointerPressed that also sees handled events.
        var pickShift = false;
        pick.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            pickShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel, handledEventsToo: true);
        pick.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            pickShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel, handledEventsToo: true);
        pick.Click += (_, e) =>
        {
            var shift = pickShift;
            pickShift = false;
            DateTime next;
            if (shift)
                next = DateTime.Today.AddDays(1).AddHours(9);
            else if (!hasDate || string.IsNullOrWhiteSpace(current))
                next = DateTime.Now.AddMinutes(1);
            else
            {
                try { datePicker.IsDropDownOpen = true; } catch { /* ignore */ }
                e.Handled = true;
                return; // user is picking from calendar
            }
            _ignoreText = true;
            datePicker.SelectedDate = next.Date;
            timeBox.Text = DueDates.FormatTime(next);
            _ignoreText = false;
            CommitDue(next, true);
            Dispatcher.UIThread.Post(() => RefreshRow(row));
            FlushSave();
            StatusText.Text = Localizer.T("date.set_due", DueDates.Format(next));
            e.Handled = true;
        };

        var badge = Recurrence.IsActive(_session.Doc.Recurrence.TryGetValue(id, out var r0) ? r0 : null)
            ? new TextBlock { Text = "🔁", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(2, 0) }
            : null;

        // ⏰ advance reminders
        var offs = ReminderPlan.OffsetsFor(_session.Doc, id);
        var alarm = new Button
        {
            Content = offs.Count > 0 ? $"⏰{offs.Count}" : "⏰",
            Classes = { "tool" },
            Padding = new Thickness(6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = _view == ViewMode.Edit
        };
        ToolTip.SetTip(alarm, offs.Count > 0
            ? Localizer.T("ui.alarm_tip_offsets", string.Join(", ", offs.OrderBy(o => o).Select(o => Localizer.T("remind.before", ReminderPlan.OffsetLabel(o)))))
            : Localizer.T("ui.alarm_tip_none"));
        alarm.Click += async (_, _) => await EditRemindersAsync(id);

        var rowPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = bg,
            Children = { datePicker, timeBox, recur, pick, alarm }
        };
        if (badge != null) rowPanel.Children.Insert(0, badge);
        if (!string.IsNullOrWhiteSpace(current) && !hasDate)
        {
            var warn = new TextBlock { Text = "⚠", Foreground = Brushes.DarkOrange, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(2, 0) };
            ToolTip.SetTip(warn, Localizer.T("date.unreadable", current));
            rowPanel.Children.Insert(0, warn);
        }
        AttachEnterNavigation(timeBox, row, column); // LostFocus then commits the time (SyncFromControls)
        return rowPanel;
    }

    /// <summary>Row still not done and due unchanged (popup keeps it even if the next fire isn't due yet).</summary>
    private bool IsStillOpenTask(int row, DateTime due)
    {
        var statusCol = _session.Doc.StatusColumnIndex();
        if (statusCol >= 0 && TaskStatuses.IsDone(_session.Doc.GetCell(row, statusCol))) return false;
        var dueCol = _session.Doc.DueColumnIndex();
        return dueCol >= 0 && DueDates.TryParse(_session.Doc.GetCell(row, dueCol), out var d) && d == due;
    }

    private async Task EditRemindersAsync(string rowId)
    {
        var row = FindRowById(rowId);
        if (row < 0) return;
        var dueCol = _session.Doc.DueColumnIndex();
        var dueValid = dueCol >= 0 && DueDates.TryParse(_session.Doc.GetCell(row, dueCol), out _);
        var titleIdx = _session.Doc.IndexOfRole(ColumnRoles.Title);
        var title = titleIdx >= 0 ? _session.Doc.GetCell(row, titleIdx).Trim() : "";
        if (title.Length == 0) title = Localizer.T("ui.row_task", row + 1);
        var dlg = new ReminderOptionsDialog(title, ReminderPlan.OffsetsFor(_session.Doc, rowId), dueValid);
        var result = await dlg.ShowDialog<List<int>?>(this);
        if (result == null) return;
        var before = Snapshot.CloneLite(_session.Doc);
        ReminderPlan.SetOffsets(_session.Doc, rowId, result, DateTime.Now);
        _undo.Push(new SnapshotAction(Localizer.T("undo.set_reminders"), before, Snapshot.CloneLite(_session.Doc)));
        FlushSave();
        var rowNow = FindRowById(rowId); // the dialog was modal, but re-find by id anyway
        if (rowNow >= 0) RefreshRow(rowNow); else Rebuild();
        StatusText.Text = result.Count == 0
            ? Localizer.T("ui.reminders_on_time", title)
            : Localizer.T("ui.reminders_set", title, string.Join(", ", result.OrderBy(o => o).Select(ReminderPlan.OffsetLabel)));
        _reminders.CheckNow();
    }

    private ControlTheme? _cellTheme;
    /// <summary>2.6.1: the lighter cell template (MainWindow.axaml, TnCellTextBox); null keeps the standard one.</summary>
    private ControlTheme? CellTheme => _cellTheme ??= this.FindResource("TnCellTextBox") as ControlTheme;

    private TextBox MakeTextBox(string text, bool header, int row, int column, IBrush bg)
    {
        var statusCol = !header ? _session.Doc.StatusColumnIndex() : -1;
        var done = !header && statusCol >= 0 && TaskStatuses.IsDone(_session.Doc.GetCell(row, statusCol));

        var box = new TextBox
        {
            Theme = CellTheme,
            Text = text,
            FontFamily = AppFonts.Thai,
            FontSize = 14,
            FontWeight = header ? FontWeight.DemiBold : FontWeight.Normal,
            Foreground = done ? DoneFg : TextFg,
            Background = bg,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            MinWidth = 96,
            MinHeight = 32,
            CornerRadius = new CornerRadius(0),
            AcceptsReturn = !header,
        };
        box.IsReadOnly = _view == ViewMode.Background;
        box.IsHitTestVisible = _view == ViewMode.Edit;
        if (done) box.Opacity = 0.7;

        box.PointerPressed += (_, e) =>
        {
            if (_view == ViewMode.Background || header || row < 0) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _session.ExtendTo(row, column);
            else _session.ChooseCell(row, column);
        };
        box.GotFocus += (_, e) =>
        {
            if (_view == ViewMode.Background) return;
            if (!header && row >= 0) { OnCellFocused(row, column, e.KeyModifiers); return; } // 2.6.0: Excel-style cells
            if (header) { OnHeaderFocused(box, column); UpdateStatus(); return; } // 2.6.0: a header selects its column
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _session.ChooseCell(row, column);
            Highlight();
            UpdateStatus();
        };
        var commit = new EditCommit(
            () => header ? (_session.Doc.Columns.ElementAtOrDefault(column) ?? "") : _session.Doc.GetCell(row, column),
            (before, after) =>
            {
                if (header) _undo.Push(new HeaderEditAction(column, before, after));
                else _undo.Push(CellEditAction.For(_session.Doc, _session.Doc.RowIdAt(row), column, before, after));
            });
        box.Tag = commit;
        box.LostFocus += (_, _) => { if (!_ignoreText) commit.Commit(box); };
        box.TextChanged += (_, _) =>
        {
            if (_ignoreText) return;
            var value = box.Text ?? "";
            // Typing (incl. Enter/newline in a data cell) is never treated as an Excel paste;
            // only a real paste is checked for a grid (see PastingFromClipboard below).
            if (value.Length > TableLimits.MaxCellChars)
            {
                _ignoreText = true;
                value = TableLimits.Clip(value, TableLimits.MaxCellChars);
                box.Text = value;
                _ignoreText = false;
                StatusText.Text = TableLimits.CellTooLongMessage;
            }
            var ok = header ? _session.SetHeader(column, value) : _session.SetCell(row, column, value);
            if (!ok) StatusText.Text = TableLimits.CellTooLongMessage;
            else
            {
                if (!header) SyncFormulaBarText(row, column, value); // 2.6.0: the formula bar follows the typing
                ScheduleSave();
                if (!header && column == _session.Doc.DueColumnIndex())
                    _reminders.CheckNow();
            }
        };
        // Real paste only: Ctrl+V / Shift+Insert (TextBox paste gestures) and the context-menu Paste
        // all go through TextBox.Paste(), which raises PastingFromClipboard first. We take over and read
        // the clipboard text ourselves, so the grid check sees only the pasted text, not box.Text.
        box.PastingFromClipboard += async (_, e) =>
        {
            if (_view != ViewMode.Edit || box.IsReadOnly) return;
            e.Handled = true;
            var clipboard = TopLevel.GetTopLevel(box)?.Clipboard;
            if (clipboard == null) { StatusText.Text = Localizer.T("ui.paste_failed"); return; }
            string? pasted;
            try { pasted = await clipboard.TryGetTextAsync(); }
            catch (Exception ex) { StatusText.Text = Localizer.T("ui.paste_failed_reason", ex.Message); return; }
            if (string.IsNullOrEmpty(pasted)) return;
            // The table may have been rebuilt or the view switched while the clipboard was read.
            if (_view != ViewMode.Edit || box.IsReadOnly || TopLevel.GetTopLevel(box) == null) return;
            if (pasted.Length > TableLimits.MaxPasteChars) { StatusText.Text = TableLimits.PasteTooBigMessage; return; }
            if (ExcelPaste.LooksLikeGrid(pasted))
            {
                commit.Commit(box); // text typed before the paste keeps its own undo entry
                _ignoreText = true;
                try { ApplyPastedGrid(box, header, row, column, pasted); }
                finally { _ignoreText = false; }
                return;
            }
            // Plain single-line text: insert at the caret / over the selection like a normal paste.
            // TextChanged then applies the cell length limit and saves.
            var current = box.Text ?? "";
            var selStart = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
            var selEnd = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), selStart, current.Length);
            box.Text = current[..selStart] + pasted + current[selEnd..];
            var caret = Math.Min(selStart + pasted.Length, (box.Text ?? "").Length);
            box.SelectionStart = box.SelectionEnd = caret;
            box.CaretIndex = caret;
        };
        // v2.3.1 fix: the old bubbling KeyDown never ran — a TextBox with AcceptsReturn handles Enter itself
        // and inserted a NEW LINE, so Enter / Shift+Enter did not move to the next cell (README: Enter = cell below).
        // Tunnel handler: Enter / Shift+Enter move (headers: Enter goes to the first row), Alt+Enter = new line in
        // a data cell (like Excel).
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Return || _view != ViewMode.Edit) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (header) return;
                e.Handled = true;
                if (box.IsReadOnly) EnterEditMode(box, null); // 2.6.0: Alt+Enter on a selected cell edits it with a new line
                var t = box.Text ?? "";
                var a = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, t.Length);
                var b = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), a, t.Length);
                box.Text = t[..a] + "\n" + t[b..];
                box.SelectionStart = box.SelectionEnd = box.CaretIndex = a + 1;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            e.Handled = true;
            commit.Commit(box);
            var up = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (header) { if (!up && _visibleRows.Count > 0) FocusCell(_visibleRows[0], column); }
            else MoveVertical(row, column, up);
        }, RoutingStrategies.Tunnel);
        if (!header && row >= 0) AttachExcelCell(box, row, column); // 2.6.0: click selects, typing replaces, F2 edits
        if (header && column >= 0) AttachExcelHeader(box, column); // 2.6.0: click selects the column, double-click renames
        return box;
    }

    /// <summary>value is the clipboard text only (never box.Text), so the box text is untouched on failure.</summary>
    private void ApplyPastedGrid(TextBox? box, bool header, int row, int column, string value)
    {
        if (value.Length > TableLimits.MaxPasteChars)
        {
            StatusText.Text = TableLimits.PasteTooBigMessage;
            return;
        }
        if (header || row < 0)
        {
            // Column header focused: never write into data rows. One pasted row = column names.
            if (!ExcelPaste.TryRead(value, out var grid, out var error))
            {
                StatusText.Text = error ?? Localizer.T("ui.paste_failed");
                return;
            }
            if (grid.Count != 1)
            {
                StatusText.Text = Localizer.T("ui.paste_header_one_row");
                return;
            }
            var names = grid[0];
            var start = Math.Max(0, column);
            var room = _session.Doc.Columns.Count - start;
            if (names.Count > room)
            {
                StatusText.Text = Localizer.T("ui.paste_header_too_many", names.Count, Math.Max(0, room));
                return;
            }
            var beforeHeaders = Snapshot.CloneLite(_session.Doc);
            for (var i = 0; i < names.Count; i++)
            {
                if (_session.SetHeader(start + i, names[i])) continue;
                Snapshot.Restore(_session.Doc, Snapshot.Serialize(beforeHeaders));
                StatusText.Text = TableLimits.CellTooLongMessage;
                return;
            }
            _undo.Push(new SnapshotAction(Localizer.T("undo.paste_excel"), beforeHeaders, Snapshot.CloneLite(_session.Doc)));
            Rebuild();
            FlushSave();
            StatusText.Text = Localizer.T("ui.paste_header_done", names.Count);
            return;
        }
        var beforeSnap = Snapshot.CloneLite(_session.Doc);
        var result = ExcelPaste.Apply(_session.Doc, row, column >= 0 ? column : 0, value);
        if (result.Kind != GridPasteKind.Applied)
        {
            StatusText.Text = result.Message ?? Localizer.T("ui.paste_failed");
            return;
        }
        _undo.Push(new SnapshotAction(Localizer.T("undo.paste_excel"), beforeSnap, Snapshot.CloneLite(_session.Doc)));
        Rebuild();
        FlushSave();
        if (!string.IsNullOrEmpty(result.Message)) StatusText.Text = result.Message;
    }

    // v2.3.1: overdue / due-today colours were computed only when a row was built, so they stayed stale when
    // time passed (and after midnight). Once a minute, re-colour only the realized rows whose colour changed.
    private DispatcherTimer? _colourClock;
    private void StartColourClock()
    {
        _colourClock ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _colourClock.Tick -= OnColourClock;
        _colourClock.Tick += OnColourClock;
        _colourClock.Start();
    }

    private void OnColourClock(object? sender, EventArgs e)
    {
        // Wallpaper mode: redraw the wallpaper when a task turns overdue or due today (WallpaperKey).
        if (_view == ViewMode.Background) { ScheduleWallpaperRefresh(); return; }
        var newDay = DaysDateChanged(); // 2.6.0: days counters move on after midnight
        if (!IsVisible) return; // hidden in the tray: nothing to paint (rows are rebuilt when shown again)
        try
        {
            var stale = new List<int>();
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            foreach (var c in RowsList.GetRealizedContainers())
                if (c is ContentPresenter { Child: Grid { Tag: int r } g } && g.Children.FirstOrDefault() is Border b &&
                    !(focused != null && focused.GetVisualAncestors().Contains(g)) && // never under the caret; next minute

                    r < _session.Doc.Rows.Count && (newDay || !ReferenceEquals(b.Background, LabelBrushFor(r, RowNumberBackground(r)))))
                    stale.Add(r);
            foreach (var r in stale) RefreshRow(r);
        }
        catch (Exception ex) { AppLog.Write("ui", "colour refresh failed: " + ex.Message); }
    }

    // v2.3.1: Highlight() was empty, so nothing showed which row Ctrl+C / − row / + row would use.
    // Now the row-number label of the active row is drawn in ActiveBg (cells keep their due/status colours).
    private IBrush LabelBrushFor(int row, IBrush normal) =>
        _view == ViewMode.Edit && IsRowSelected(row) ? ActiveBg : normal; // 2.6.0: every row of the selected block

    /// <summary>Enter / Shift+Enter: next / previous VISIBLE row (hidden done rows are skipped).</summary>
    private void MoveVertical(int row, int column, bool up)
    {
        var vi = _visibleRows.IndexOf(row);
        if (vi < 0) { FocusCell(row + (up ? -1 : 1), column); return; }
        var next = Math.Clamp(vi + (up ? -1 : 1), 0, _visibleRows.Count - 1);
        FocusCell(_visibleRows[next], column);
    }

    /// <summary>Enter navigation for non-text cells (status drop-down, due time box).</summary>
    private void AttachEnterNavigation(Control control, int row, int column, Func<bool>? skip = null)
    {
        control.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Return || _view != ViewMode.Edit || skip?.Invoke() == true) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
            e.Handled = true;
            MoveVertical(row, column, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }, RoutingStrategies.Tunnel);
    }

    private void Highlight() => RefreshSelectionVisuals(); // 2.6.0: the selected block, not only the active row

    private void FocusActiveCell()
    {
        var r = Math.Max(0, _session.ActiveRow);
        var c = Math.Max(0, _session.ActiveCol);
        FocusCell(r, c);
    }

    /// <param name="keepRange">2.6.0: focus the active cell of a selected block without shrinking the block.</param>
    private void FocusCell(int row, int column, bool keepRange = false)
    {
        row = Math.Clamp(row, 0, Math.Max(0, _session.Doc.Rows.Count - 1));
        column = Math.Clamp(column, 0, Math.Max(0, _session.Doc.Columns.Count - 1));
        column = NearestVisibleGroupColumn(column);
        if (keepRange)
        {
            _keepRange = true;
            Dispatcher.UIThread.Post(() => _keepRange = false, DispatcherPriority.Background); // no focus change came
        }
        else _session.ChooseCell(row, column);
        var vi = _visibleRows.IndexOf(row);
        if (vi < 0) return;
        RowsList.ScrollIntoView(vi);
        // Container exists after the layout pass that ScrollIntoView triggers.
        Dispatcher.UIThread.Post(() =>
        {
            if (RowsList.ContainerFromIndex(vi) is not ContentPresenter { Child: Grid g }) return;
            var cell = g.Children.FirstOrDefault(ch => Grid.GetColumn(ch) == column + 1);
            var target = cell is null ? null :
                (cell as InputElement is { Focusable: true } ie ? ie : cell.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(x => x.Focusable && x.IsEffectivelyEnabled));
            target?.Focus(NavigationMethod.Directional);
            if (target is TextBox tb) tb.CaretIndex = (tb.Text ?? "").Length; // v2.3.1: caret at the end
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Ctrl+Home = first cell, Ctrl+End = last cell of the last visible row (Excel-like).</summary>
    private void OnGridKeyTunnel(object? sender, KeyEventArgs e)
    {
        if (_view == ViewMode.Background || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key is not (Key.Home or Key.End) || _visibleRows.Count == 0) return;
        CommitFocusedEdit();
        e.Handled = true;
        if (e.Key == Key.Home) FocusCell(_visibleRows[0], 0);
        else FocusCell(_visibleRows[^1], _session.Doc.Columns.Count - 1);
        UpdateStatus();
    }

    private void FitColumns()
    {
        var width = TableScroll.Bounds.Width;
        if (!double.IsNaN(width) && width > 1)
            TableHost.MinWidth = Math.Max(0, width - 2);
    }

    private void UpdateStatus()
    {
        if (_saveFailureMessage != null)
        {
            StatusText.Text = _saveFailureMessage;
            return;
        }
        if (_loadWarning != null && StatusText.Tag as string != "shown")
        {
            StatusText.Text = _loadWarning;
            StatusText.Tag = "shown";
            return;
        }
        var onTop = Topmost ? Localizer.T("status.on_top") : "";
        StatusText.Text = Localizer.T("status.summary", _session.Doc.Rows.Count, _session.Doc.Columns.Count, onTop);
    }

    private RecoveryRoutes CurrentRoutes() => new(_trayReady, Program.SignalReady);
    private void ShowHelp() => HelpWindow.Show(Localizer.T("tip.help"), AppInfo.HelpText, _settings.Scale / 100.0); // 2.6.0: topics that open and close
    private void ShowLog() => InfoWindow.Show(Localizer.T("tray.log"), AppInfo.LogText); // v2.4.2: same key as the tray Log item

    /// <summary>
    /// The editor window. Coming back from wallpaper mode puts the user's own wallpaper back first (also after a
    /// crash: wallpaper.json says what to put back).
    /// </summary>
    private void EnterEdit(string? notice)
    {
        _wallpaperTimer?.Stop();
        notice = JoinNotice(notice, RestorePendingWallpaper());
        _view = ViewMode.Edit;
        _session.Doc.View = ViewMode.Edit;
        Topmost = false;
        ApplyTaskbarPresence(); // v2.4.2: edit mode has a taskbar button
        ApplyChrome();
        Show();
        WatchDrawing("editor", logFirstFrame: !_startedUpEditor); // 2.6.0: logged if never drawn; the first time also how fast
        _startedUpEditor = true;
        ApplyRequestedLayer();
        EnsureVisible();
        Activate(); // v2.3.1: after the layer/position is final (was before ApplyRequestedLayer)
        UpdateTrayTip();
        if (!string.IsNullOrEmpty(notice)) ShowNotice(notice);
        FlushSave();
        UpdateStatus();
    }

    /// <summary>v2.3.1: hotkey / tray / second start while the editor is already open and visible → just
    /// bring it to the front (was a full EnterEdit: detach, chrome, rebuild, save on every press).</summary>
    private void ShowEditor()
    {
        if (_view == ViewMode.Edit && IsVisible) { Activate(); return; }
        EnterEdit(null);
    }

    private void CenterEditor()
    {
        EnterEdit(Localizer.T("ui.moved_center"));
        var areas = WorkAreas();
        ApplyRestored(WindowPlacement.Center(PrimaryWorkArea(areas), _session.Doc.Window.Width, _session.Doc.Window.Height));
        ShowNotice(Localizer.T("ui.moved_center"));
    }

    private void EnsureVisible()
    {
        var w = _session.Doc.Window;
        if (w.X is not int x || w.Y is not int y) return;
        var areas = WorkAreas();
        var placed = WindowPlacement.Restore(x, y, w.Width, w.Height, areas, PrimaryWorkArea(areas));
        ApplyRestored(placed);
        if (placed.Recentered)
            ShowNotice(Localizer.T("ui.offscreen_opened"));
    }

    private void ApplyRestored(WindowPlacement.RestoreResult placed)
    {
        _applyingPlacement = true;
        Position = new PixelPoint(placed.X, placed.Y);
        Width = placed.Width;
        Height = placed.Height;
        _applyingPlacement = false;
        _session.Doc.Window.X = placed.X;
        _session.Doc.Window.Y = placed.Y;
        _session.Doc.Window.Width = placed.Width;
        _session.Doc.Window.Height = placed.Height;
    }

    private List<WindowPlacement.IntRect> WorkAreas()
    {
        if (Screens == null) return [];
        return Screens.All
            .Select(s => new WindowPlacement.IntRect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height))
            .Where(a => a.Width > 0 && a.Height > 0)
            .ToList();
    }

    private WindowPlacement.IntRect PrimaryWorkArea(IReadOnlyList<WindowPlacement.IntRect> areas)
    {
        var screen = Screens?.Primary;
        if (screen != null)
        {
            var r = new WindowPlacement.IntRect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
            if (r.Width > 0 && r.Height > 0) return r;
        }
        return areas.Count > 0 ? areas[0] : new WindowPlacement.IntRect(0, 0, 720, 480);
    }

    private void ApplyChrome()
    {
        var edit = _view == ViewMode.Edit;
        EditTools.IsVisible = edit;
        ToolScroll.IsVisible = edit;
        FormulaBar.IsVisible = edit; // 2.6.0
        ApplyActivityVisibility();
        if (!edit) CloseFind();
        IsHitTestVisible = edit;
        CanResize = edit && !_session.Doc.Window.Locked;
        Rebuild();
        RenderAlertBoxes();
    }

    /// <summary>v2.4.2: taskbar button for the current mode (TaskbarPresence): edit = shown, background = tray only.</summary>
    private bool ApplyTaskbarPresence()
    {
        var show = TaskbarPresence.ShowInTaskbar(_view);
        if (!show) return HideFromTaskbar();
        ShowInTaskbar = true;
        if (!OperatingSystem.IsWindows()) return true;
        return WindowsTaskbar.Show(WindowHandle());
    }

    private bool HideFromTaskbar()
    {
        ShowInTaskbar = false;
        if (!OperatingSystem.IsWindows()) return true;
        return WindowsTaskbar.Hide(WindowHandle());
    }

    private bool TryCreateTray()
    {
        try
        {
            var edit = new NativeMenuItem(Localizer.T("tray.edit"));
            edit.Click += (_, _) => ShowEditor();
            var help = new NativeMenuItem(Localizer.T("tray.help"));
            help.Click += (_, _) => ShowHelp();
            var log = new NativeMenuItem(Localizer.T("tray.log"));
            log.Click += (_, _) => ShowLog();
            var export = new NativeMenuItem(Localizer.T("tray.export"));
            export.Click += async (_, _) => { EnterEdit(null); await ExportAsync(); };
            var langMenu = new NativeMenu();
            foreach (var code in Localizer.Supported) // v2.4.2: one radio item per embedded lang/*.json
            {
                var c = code;
                var item = new NativeMenuItem(Localizer.LanguageName(c)) { ToggleType = MenuItemToggleType.Radio, IsChecked = Localizer.Current == c };
                item.Click += (_, _) => SwitchLanguage(c);
                langMenu.Items.Add(item);
            }
            var lang = new NativeMenuItem(Localizer.T("tray.language")) { Menu = langMenu };
            var zin = new NativeMenuItem(Localizer.T("tray.zoom_in"));
            zin.Click += (_, _) => SetZoom(_settings.Scale + AppSettings.ScaleStep);
            var zout = new NativeMenuItem(Localizer.T("tray.zoom_out"));
            zout.Click += (_, _) => SetZoom(_settings.Scale - AppSettings.ScaleStep);
            var zreset = new NativeMenuItem(Localizer.T("tray.zoom_reset"));
            zreset.Click += (_, _) => SetZoom(100);
            var center = new NativeMenuItem(Localizer.T("tray.center"));
            center.Click += (_, _) => CenterEditor();
            var quit = new NativeMenuItem(Localizer.T("tray.quit"));
            quit.Click += (_, _) => QuitApp();
            var menu = new NativeMenu();
            foreach (var item in new NativeMenuItemBase[] { edit, help, log, export, new NativeMenuItemSeparator(), lang, zin, zout, zreset, new NativeMenuItemSeparator(), center, quit })
                menu.Items.Add(item);
            if (_tray != null) { _tray.Menu = menu; _tray.ToolTipText = TrayTip(); return true; }
            _tray = new TrayIcon { ToolTipText = TrayTip(), Menu = menu, IsVisible = true };
            _tray.Clicked += (_, _) => ShowEditor();
            using var stream = AssetLoader.Open(new Uri("avares://TableNote/Assets/tray.png"));
            _tray.Icon = new WindowIcon(stream);
            TrayIcon.SetIcons(Application.Current, [_tray]);
            return true;
        }
        catch { return false; }
    }

    private bool TryRegisterHotkey()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var ok = WindowsHotkey.TryRegister(WindowHandle(), out var collided);
        _hotkeyCollided = collided;
        return ok;
    }

    private async Task CopyAsync(bool all, bool headers)
    {
        if (!ExcelCopy.AllowsClipboard(_view)) return;
        var text = all
            ? ExcelCopy.WholeTable(_session.Doc, headers)
            : ExcelCopy.Selection(_session.Doc, _session.AnchorRow, _session.AnchorCol, _session.ActiveRow, _session.ActiveCol);
        if (text == null) { StatusText.Text = Localizer.T("ui.copy_too_big"); return; }
        if (Clipboard == null) { StatusText.Text = Localizer.T("ui.copy_failed"); return; }
        await Clipboard.SetTextAsync(text);
        StatusText.Text = headers
            ? Localizer.T("ui.copied_table_headers")
            : Localizer.T(all ? "ui.copied_table" : "ui.copied_selection");
    }

    private static string? JoinNotice(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first)) return second;
        if (string.IsNullOrEmpty(second)) return first;
        return first + " " + second;
    }
}
