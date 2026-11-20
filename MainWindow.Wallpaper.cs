using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WallpaperTests")]

namespace TableNote;

/// <summary>
/// Wallpaper mode (2.6.0, replaces "pin to desktop"): the table is drawn over the user's own wallpaper, full screen,
/// on the monitor the window is on, and the editor goes to the tray. The tray icon, Ctrl+Shift+E or starting
/// TableNote again bring the editor back and put the user's wallpaper back (<see cref="EnterEdit"/>).
/// The mode is kept in note.json like the old background mode; <see cref="WallpaperMode"/> does the Windows side.
/// </summary>
public partial class MainWindow
{
    /// <summary>Test seam: a fake desktop instead of the real one.</summary>
    internal static Func<IWallpaperHost>? WallpaperHostOverride { get; set; }

    private WallpaperMode? _wallpaperMode;
    private DispatcherTimer? _wallpaperTimer;
    private string? _wallpaperKey;
    private bool _wallpaperBusy;

    private WallpaperMode Wallpaper => _wallpaperMode ??= new WallpaperMode(
        WallpaperHostOverride?.Invoke() ?? WallpaperHosts.ForCurrentMachine(), WallpaperFiles.Folder(_path));

    private void InitWallpaper() => WallpaperButton.Click += async (_, _) => await OnWallpaperClickAsync();

    private void ApplyWallpaperTexts()
    {
        WallpaperButton.Content = Localizer.T("toolbar.wallpaper");
        ToolTip.SetTip(WallpaperButton, Localizer.T("tip.wallpaper"));
    }

    /// <summary>Toolbar button / menu item: put the table on the wallpaper and hide the editor.</summary>
    private async Task OnWallpaperClickAsync()
    {
        if (_view != ViewMode.Edit || _wallpaperBusy) return;
        var block = RecoveryGate.BlockReason(CurrentRoutes());
        if (block != null) { ShowNotice(block); return; }
        CommitFocusedEdit();
        CaptureBounds();
        FlushSave();
        _wallpaperBusy = true;
        try
        {
            StatusText.Text = Localizer.T("wallpaper.working");
            var outcome = ApplyWallpaper(confirmed: false);
            if (outcome.NeedsConfirm)
            {
                var go = await MessageBox.Show(this, Localizer.T("wallpaper.spotlight_confirm"), Localizer.T("wallpaper.title"),
                    Localizer.T("wallpaper.continue"), Localizer.T("common.cancel"));
                if (!go) { UpdateStatus(); return; }
                outcome = ApplyWallpaper(confirmed: true);
            }
            if (!outcome.Ok)
            {
                UpdateStatus();
                ShowNotice(outcome.Message);
                return;
            }
            EnterWallpaperView();
            ShowWallpaperTipOnce();
        }
        finally { _wallpaperBusy = false; }
    }

    /// <summary>Draws the table picture and sets it (the first time, or again after a change).</summary>
    private WallpaperOutcome ApplyWallpaper(bool confirmed)
    {
        try
        {
            var screens = WallpaperScreens();
            using (UiActivity.Scope("wallpaper"))
                return Wallpaper.Apply(WindowPixelRect(screens), screens, RenderWallpaper, confirmed);
        }
        catch (Exception ex)
        {
            // Never leave the editor half-switched: report it and stay (or go back) in the editor.
            AppLog.Write("wallpaper", "failed: " + ex.GetType().Name + ": " + ex.Message);
            return new WallpaperOutcome(false, Localizer.T("wallpaper.set_failed", ex.Message), ex.Message);
        }
    }

    private List<WallpaperScreen> WallpaperScreens() =>
        Screens?.All.Select(s => new WallpaperScreen(s.Bounds, s.WorkingArea, s.Scaling > 0 ? s.Scaling : 1)).ToList() ?? [];

    /// <summary>The editor's rectangle in screen pixels: its saved position, and its size (DIPs) × its monitor's scale.</summary>
    private PixelRect WindowPixelRect(IReadOnlyList<WallpaperScreen> screens)
    {
        var w = _session.Doc.Window;
        var x = w.X ?? Position.X;
        var y = w.Y ?? Position.Y;
        var i = WallpaperGeometry.Pick(screens.Select(s => s.Bounds).ToList(), new PixelRect(x, y, 240, 120));
        var scale = i >= 0 ? screens[i].Scaling : 1;
        var width = Math.Max(240, w.Width > 0 ? w.Width : (int)Width);
        var height = Math.Max(160, w.Height > 0 ? w.Height : (int)Height);
        return new PixelRect(x, y, (int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    /// <summary>The card (alert boxes and table) at the window's place and size, scaled like the window.</summary>
    private byte[]? RenderWallpaper(WallpaperScene scene)
    {
        var scale = Math.Max(0.5, scene.Screen.Scaling * (_settings.Scale / 100.0));
        var box = WallpaperGeometry.CardBox(scene.Window, scene.Screen.WorkArea);
        var background = Background ?? new SolidColorBrush(Color.Parse("#FFF6C2"));
        var card = WallpaperImage.BuildCard(_session.Doc, WallpaperOptions(), new Size(box.Width / scale, box.Height / scale),
            background, PartColorStore.Get);
        return WallpaperImage.Compose(scene, card, new PixelPoint(box.X - scene.Area.X, box.Y - scene.Area.Y), scale);
    }

    /// <summary>Same colours, collapsed columns and row heights as image export; rows follow hide-done.</summary>
    private ViewImageOptions WallpaperOptions()
    {
        var paints = ImagePartColors.From(_session.Doc);
        return new ViewImageOptions
        {
            Now = DateTime.Now,
            Zoom = _settings.Scale / 100.0,
            IsHidden = ColumnGroupOps.IsHidden,
            RowHeight = SizeSpec.StoredRowHeight,
            CellColors = paints.CellColors,
            CellTextColors = paints.CellTextColors,
            HeaderColors = paints.HeaderColors,
            RowNumberColors = paints.RowNumberColors
        };
    }

    /// <summary>The table is on the wallpaper: the editor goes to the tray (the mode is saved for the next start).</summary>
    private void EnterWallpaperView()
    {
        _view = ViewMode.Background;
        _session.Doc.View = ViewMode.Background;
        ShowNotice(null);
        HideFromTaskbar();
        // Posted: at start-up this runs inside OnOpened, while Avalonia is still showing the window.
        Dispatcher.UIThread.Post(() => { if (_view == ViewMode.Background) Hide(); });
        UpdateTrayTip();
        FlushSave();
        _wallpaperKey = WallpaperKey(); // after the save: the picture that is up shows this state
        AppLog.Write("wallpaper", "wallpaper mode on");
    }

    /// <summary>Puts the user's wallpaper back if a table picture may be up. Returns a message for the banner, or null.</summary>
    private string? RestorePendingWallpaper()
    {
        try
        {
            if (!Wallpaper.Pending) return null;
            using var _ = UiActivity.Scope("wallpaper restore");
            var outcome = Wallpaper.Restore();
            if (outcome.Ok && outcome.Changed) AppLog.Write("wallpaper", "wallpaper mode off");
            return outcome.Message;
        }
        catch (Exception ex)
        {
            AppLog.Write("wallpaper", "restore failed: " + ex.GetType().Name + ": " + ex.Message);
            return Localizer.T("wallpaper.restore_failed", ex.Message);
        }
    }

    /// <summary>Exit / sign-out: the user's wallpaper goes back, wallpaper mode stays on in note.json.</summary>
    private void RestoreWallpaperOnExit()
    {
        _wallpaperTimer?.Stop();
        var message = RestorePendingWallpaper();
        if (message != null) AppLog.Write("wallpaper", "on exit: " + message);
    }

    /// <summary>Redraw in 1.5 s. Further changes in that time ride along (the timer is not pushed back, so a run of
    /// saves can't keep the wallpaper out of date).</summary>
    private void ScheduleWallpaperRefresh()
    {
        if (_view != ViewMode.Background) return;
        if (_wallpaperTimer == null)
        {
            _wallpaperTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _wallpaperTimer.Tick += (_, _) =>
            {
                _wallpaperTimer.Stop();
                RefreshWallpaper();
            };
        }
        if (!_wallpaperTimer.IsEnabled) _wallpaperTimer.Start();
    }

    /// <summary>Redraws the wallpaper when something it shows changed. A failure is logged; the next change tries again.</summary>
    private void RefreshWallpaper()
    {
        if (_view != ViewMode.Background) return;
        var key = WallpaperKey();
        if (key == _wallpaperKey) return;
        var outcome = ApplyWallpaper(confirmed: false);
        // Spotlight switched on meanwhile (NeedsConfirm): the user's choice stays, nothing is drawn over it.
        if (outcome.Ok || outcome.NeedsConfirm) _wallpaperKey = key;
    }

    /// <summary>Everything the picture depends on, so the minute clock redraws only when a colour or text changed.</summary>
    private string WallpaperKey()
    {
        var doc = _session.Doc;
        var key = new StringBuilder(PreserveKey(doc));
        var now = DateTime.Now;
        key.Append('|').Append(doc.HideDone).Append('|').Append(now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
        var due = doc.DueColumnIndex();
        if (due >= 0)
            for (var r = 0; r < doc.Rows.Count; r++)
                key.Append(DueDates.TryParse(doc.GetCell(r, due), out var d)
                    ? DueDates.IsOverdue(d, now) ? 'o' : DueDates.IsDueToday(d, now) ? 't' : '-'
                    : ' ');
        key.Append('|').Append(Localizer.Current).Append('|').Append(_settings.Scale);
        foreach (var s in WallpaperScreens()) key.Append('|').Append(s.Bounds).Append(s.WorkArea).Append(s.Scaling);
        var w = doc.Window;
        key.Append('|').Append(w.X).Append(',').Append(w.Y).Append(',').Append(w.Width).Append(',').Append(w.Height);
        return key.ToString();
    }

    private string TrayTip() => Localizer.T(_view == ViewMode.Background ? "tray.tip_wallpaper" : "app.title");

    private void UpdateTrayTip()
    {
        if (_tray != null) _tray.ToolTipText = TrayTip();
    }

    /// <summary>First time only: where the editor went and how to get it (and the old wallpaper) back.</summary>
    private void ShowWallpaperTipOnce()
    {
        if (_settings.WallpaperTipShown) return;
        _settings.WallpaperTipShown = true;
        _settings.Save(_path);
        var ok = new Button { Content = Localizer.T("ui.got_it"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var body = Localizer.T("wallpaper.tip_body", Localizer.T("tray.edit"), Localizer.T("tray.quit"));
        if (_hotkeyReady) body += "\n" + Localizer.T("wallpaper.tip_hotkey");
        var w = new Window
        {
            Title = Localizer.T("wallpaper.title"),
            Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = AppFonts.Thai,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Text = body },
                    ok
                }
            }
        };
        ok.Click += (_, _) => w.Close();
        w.Show();
    }
}
