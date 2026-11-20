using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// 2.6.0: loading screen while TableNote opens: the Table Note logo, a moving bar, "Opening your table…" and the
/// version, centred on the screen. Shown for a normal start, not for the hidden sign-in start (--startup);
/// <see cref="App"/> builds the editor once it has been drawn and closes it when the editor is up.
/// </summary>
public sealed class SplashWindow : Window
{
    public const string LogoUri = "avares://TableNote/Assets/Brand/splash-logo.png";
    /// <summary>Shown at least this long, so a fast start doesn't just flicker.</summary>
    public static TimeSpan MinimumTime { get; set; } = TimeSpan.FromMilliseconds(1200);
    /// <summary>Longest wait for the first frame before the editor is built anyway.</summary>
    public static TimeSpan FirstFrameTimeout { get; set; } = TimeSpan.FromMilliseconds(700);

    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#D4A94A"));
    private static readonly IBrush GoldTrack = new SolidColorBrush(Color.Parse("#F3E6C4"));
    private static readonly IBrush SoftFg = new SolidColorBrush(Color.Parse("#7A6A48"));
    private readonly Stopwatch _shown = new();
    private bool _closing;

    public SplashWindow()
    {
        Title = "Table Note";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = AppFonts.Thai;

        var logo = new Image { Width = 260, Height = 256, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        try
        {
            using var stream = AssetLoader.Open(new Uri(LogoUri));
            logo.Source = new Bitmap(stream);
        }
        catch (Exception ex) { AppLog.Write("splash", "logo not loaded: " + ex.Message); }

        var bar = new ProgressBar
        {
            IsIndeterminate = true, Width = 220, Height = 4, MinHeight = 4,
            Foreground = Gold, Background = GoldTrack, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var loading = new TextBlock
        {
            Text = Localizer.T("splash.loading"), FontSize = 14, Foreground = SoftFg,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        };
        var version = new TextBlock
        {
            Text = Localizer.T("splash.version", AppInfo.Version), FontSize = 12, Foreground = SoftFg, Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
        };
        Content = new Border
        {
            BorderBrush = Gold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(56, 30, 56, 22),
            Child = new StackPanel { Children = { logo, bar, loading, version } }
        };
        Opened += (_, _) => _shown.Start();
    }

    /// <summary>Calls <paramref name="next"/> once the splash has been drawn (two frames), or after
    /// <see cref="FirstFrameTimeout"/> when no frame comes (the first window also sets up the graphics, which can take a moment).</summary>
    public void WhenDrawn(Action next)
    {
        var done = false;
        void Go()
        {
            if (done) return;
            done = true;
            Dispatcher.UIThread.Post(next, DispatcherPriority.Background);
        }
        try { RequestAnimationFrame(_ => RequestAnimationFrame(_ => Go())); }
        catch (Exception ex) { AppLog.Write("splash", "no frame callback: " + ex.Message); }
        DispatcherTimer.RunOnce(Go, FirstFrameTimeout);
    }

    /// <summary>Runs <paramref name="next"/> once the splash has been up for <see cref="MinimumTime"/>.</summary>
    public void AfterMinimumTime(Action next)
    {
        var wait = MinimumTime - _shown.Elapsed;
        if (wait <= TimeSpan.Zero) next();
        else DispatcherTimer.RunOnce(next, wait);
    }

    /// <summary>Closes when <paramref name="main"/> has drawn its first frames (so there is no gap between the two), or after
    /// <see cref="FirstFrameTimeout"/> (wallpaper mode hides the editor at once). Then <paramref name="after"/> runs.</summary>
    public void CloseAfterFirstFrameOf(Window main, Action? after = null)
    {
        if (_closing) return;
        _closing = true;
        var done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            try { Close(); }
            catch (Exception ex) { AppLog.Write("splash", "close failed: " + ex.Message); }
            after?.Invoke();
        }
        try { main.RequestAnimationFrame(_ => main.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(Finish))); }
        catch (Exception ex) { AppLog.Write("splash", "no frame callback on the editor: " + ex.Message); }
        DispatcherTimer.RunOnce(Finish, FirstFrameTimeout);
    }
}
