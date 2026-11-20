using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TableNote;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Quit only via the tray menu (explicit Shutdown); closing the window hides it to the tray.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // 2.6.0: a normal start shows the loading screen first; the sign-in start stays quietly in the tray.
            if (Program.StartHidden) desktop.MainWindow = new MainWindow();
            else StartWithSplash(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Loading screen first, then the editor once the splash is on screen (opening note.json and building the
    /// table take a moment, and the editor's window shows only when that is done). The splash closes when the editor
    /// is up, after <see cref="SplashWindow.MinimumTime"/> at the earliest.</summary>
    private static void StartWithSplash(IClassicDesktopStyleApplicationLifetime desktop)
    {
        SplashWindow? splash = null;
        try
        {
            Localizer.SetLanguage(AppSettings.Load(NoteStore.DefaultPath()).Language); // the splash speaks the user's language
            splash = new SplashWindow();
            splash.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("splash", "not shown: " + ex.GetType().Name + ": " + ex.Message);
            try { splash?.Close(); } catch { /* ignore */ }
            splash = null;
        }

        void OpenEditor(bool show)
        {
            MainWindow main;
            try
            {
                using (UiActivity.Scope("start: build editor"))
                    main = new MainWindow();
            }
            catch (Exception ex) when (show)
            {
                // Same end as before the splash existed: the start fails, the app does not linger without a window.
                AppLog.Write("start", "editor could not be built: " + ex.GetType().Name + ": " + ex.Message);
                try { splash?.Close(); } catch { /* ignore */ }
                desktop.Shutdown(1);
                return;
            }
            desktop.MainWindow = main;
            if (!show) return; // no splash: the lifetime shows MainWindow when it starts, as before
            // The editor is built (note.json read, table laid out) while the splash is up; it is shown once the splash has
            // been up long enough, and the splash goes when the editor has drawn, so there is no empty moment between them.
            splash!.AfterMinimumTime(() =>
            {
                if (main.IsVisible) // a second start ("show the editor") came first and showed it already
                {
                    splash.CloseAfterFirstFrameOf(main, () => main.Activate());
                    return;
                }
                void Ready(object? sender, EventArgs e)
                {
                    main.Opened -= Ready;
                    splash.CloseAfterFirstFrameOf(main, () => { if (main.IsVisible) main.Activate(); });
                }
                main.Opened += Ready;
                using (UiActivity.Scope("start: show editor"))
                    main.Show();
            });
        }

        if (splash == null) OpenEditor(show: false);
        else splash.WhenDrawn(() => OpenEditor(show: true));
    }
}
