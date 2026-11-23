using Avalonia.Controls;

namespace TableNote;

/// <summary>2.6.1.1: the drawing pad (Draw button, Table → Drawing pad…). One window at a time; kept in sketch.json.</summary>
public partial class MainWindow
{
    private SketchWindow? _sketchWindow;

    private void InitSketch() => SketchButton.Click += (_, _) => OpenSketch();

    private void ApplySketchTexts()
    {
        SketchButton.Content = Localizer.T("toolbar.sketch");
        ToolTip.SetTip(SketchButton, Localizer.T("tip.sketch"));
    }

    private void OpenSketch()
    {
        if (_view != ViewMode.Edit) return;
        if (_sketchWindow != null)
        {
            if (_sketchWindow.WindowState == WindowState.Minimized) _sketchWindow.WindowState = WindowState.Normal;
            _sketchWindow.Activate();
            return;
        }
        var window = new SketchWindow(SketchStore.PathFor(_path)) { Icon = Icon };
        window.Closed += (_, _) => { if (ReferenceEquals(_sketchWindow, window)) _sketchWindow = null; };
        _sketchWindow = window;
        window.Show(); // not owned: it stays when the editor goes to the tray or to wallpaper mode
        ActivityLog.Shared.Add(ActivityLevel.Info, Localizer.T("sketch.activity_opened"));
    }

    /// <summary>On exit: the drawing pad is saved and closed with TableNote.</summary>
    private void CloseSketch()
    {
        if (_sketchWindow == null) return;
        _sketchWindow.SaveNow();
        _sketchWindow.Close();
        _sketchWindow = null;
    }
}
