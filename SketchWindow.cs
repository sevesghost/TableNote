using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// 2.6.1.1: drawing pad for handwritten notes (Table → Drawing pad…, or the Draw button): pen, eraser, ruler, clear
/// all, pen colour, size and sharpness 0–100, undo / redo, pages, lined paper. Kept in sketch.json next to note.json,
/// saved by itself shortly after every change and when the window closes.
/// </summary>
public sealed class SketchWindow : Window
{
    public const int MaxUndo = 100;
    private static readonly IBrush Bar = new SolidColorBrush(Color.Parse("#F6E7A4"));
    private static readonly IBrush ToolBg = new SolidColorBrush(Color.Parse("#F0D78A"));
    private static readonly IBrush ToolOn = new SolidColorBrush(Color.Parse("#C08A1E"));
    private static readonly IBrush ToolFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush Hint = new SolidColorBrush(Color.Parse("#6B5E48"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#B5530B"));
    private static readonly IBrush SwatchEdge = new SolidColorBrush(Color.Parse("#8A7A55"));
    private static readonly IBrush SwatchPicked = new SolidColorBrush(Color.Parse("#3B3222"));

    private readonly string _file;
    private readonly SketchDocument _doc;
    private readonly SketchCanvas _canvas = new();
    private readonly Dictionary<SketchTool, ToggleButton> _toolButtons = [];
    private readonly List<(string Color, Border Swatch)> _swatches = [];
    private readonly TextBox _hex = new() { Width = 84, FontSize = 12, MinHeight = 0, Height = 26, Padding = new Thickness(5, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _currentColor = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = SwatchEdge };
    private readonly Slider _size = new() { Minimum = SketchOps.MinWidth, Maximum = SketchOps.MaxWidth, Width = 110, SmallChange = 1, LargeChange = 5, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sizeText = new() { MinWidth = 26, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly Slider _sharp = new() { Minimum = 0, Maximum = 100, Width = 120, SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sharpText = new() { MinWidth = 26, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock _sizeLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) };
    private readonly TextBlock _sharpLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _undo = new();
    private readonly Button _redo = new();
    private readonly Button _clear = new();
    private readonly CheckBox _lined = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinHeight = 0 };
    private readonly Button _prev = new();
    private readonly Button _next = new();
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(4, 0) };
    private readonly Button _addPage = new();
    private readonly Button _deletePage = new();
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Hint, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly List<(List<List<SketchStroke>> Pages, int Page)> _undoSteps = [];
    private readonly List<(List<List<SketchStroke>> Pages, int Page)> _redoSteps = [];
    private bool _loading;
    private bool _asking;

    public SketchWindow(string file)
    {
        _file = file;
        Title = Localizer.T("sketch.title");
        Width = 980;
        Height = 680;
        MinWidth = 560;
        MinHeight = 360;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _doc = SketchStore.Load(file, out var warning);
        _canvas.Document = _doc;
        _canvas.Committing += PushUndo;
        _canvas.Changed += ScheduleSave;
        _canvas.Full += () => Say(Localizer.T("sketch.full"), true);
        _canvas.RulerMoved += (len, angle) =>
        {
            if (len < 0) ShowHint();
            else Say(Localizer.T("sketch.ruler_label", Math.Round(len).ToString(System.Globalization.CultureInfo.InvariantCulture), angle));
        };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, key) in new[] { (SketchTool.Pen, "sketch.pen"), (SketchTool.Eraser, "sketch.eraser"), (SketchTool.Ruler, "sketch.ruler") })
        {
            var t = tool;
            var b = new ToggleButton { Content = Localizer.T(key), Tag = key };
            StyleTool(b);
            ToolTip.SetTip(b, Localizer.T(key + "_tip"));
            b.Click += (_, _) => SetTool(t);
            _toolButtons[tool] = b;
            tools.Children.Add(b);
        }
        tools.Children.Add(Sep());

        tools.Children.Add(new TextBlock { Text = Localizer.T("sketch.color"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
        foreach (var color in SketchOps.Palette)
        {
            var c = color;
            var swatch = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(1, 0),
                Background = new SolidColorBrush(Color.Parse(c)), BorderBrush = SwatchEdge, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(swatch, c);
            swatch.PointerPressed += (_, e) => { SetColor(c); e.Handled = true; };
            _swatches.Add((c, swatch));
            tools.Children.Add(swatch);
        }
        _hex.Margin = new Thickness(6, 0, 4, 0);
        ToolTip.SetTip(_hex, Localizer.T("sketch.hex_tip"));
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyHex(); _canvas.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _hex.Text = _doc.PenColor; _canvas.Focus(); e.Handled = true; }
        };
        _hex.LostFocus += (_, _) => ApplyHex();
        tools.Children.Add(_hex);
        tools.Children.Add(_currentColor);
        tools.Children.Add(Sep());

        tools.Children.Add(_sizeLabel);
        tools.Children.Add(_size);
        tools.Children.Add(_sizeText);
        tools.Children.Add(Sep());
        _sharpLabel.Text = Localizer.T("sketch.sharpness");
        ToolTip.SetTip(_sharp, Localizer.T("sketch.sharpness_tip"));
        ToolTip.SetTip(_sharpLabel, Localizer.T("sketch.sharpness_tip"));
        tools.Children.Add(_sharpLabel);
        tools.Children.Add(_sharp);
        tools.Children.Add(_sharpText);
        tools.Children.Add(Sep());

        _undo.Content = "↩"; ToolTip.SetTip(_undo, Localizer.T("sketch.undo_tip"));
        _redo.Content = "↪"; ToolTip.SetTip(_redo, Localizer.T("sketch.redo_tip"));
        _clear.Content = Localizer.T("sketch.clear"); ToolTip.SetTip(_clear, Localizer.T("sketch.clear_tip"));
        foreach (var b in new[] { _undo, _redo, _clear }) { StyleTool(b); tools.Children.Add(b); }
        _undo.Click += (_, _) => Undo();
        _redo.Click += (_, _) => Redo();
        _clear.Click += async (_, _) => await ClearAllAsync();
        tools.Children.Add(Sep());

        _lined.Content = Localizer.T("sketch.lined");
        _lined.Foreground = ToolFg;
        _lined.Margin = new Thickness(0, 0, 6, 0);
        _lined.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _doc.Lined = _lined.IsChecked == true;
            _canvas.InvalidateVisual();
            ScheduleSave();
        };
        tools.Children.Add(_lined);
        tools.Children.Add(Sep());

        _prev.Content = "◀"; ToolTip.SetTip(_prev, Localizer.T("sketch.prev_tip"));
        _next.Content = "▶"; ToolTip.SetTip(_next, Localizer.T("sketch.next_tip"));
        _addPage.Content = Localizer.T("sketch.add_page");
        _deletePage.Content = Localizer.T("sketch.delete_page");
        foreach (var b in new[] { _prev, _next, _addPage, _deletePage }) StyleTool(b);
        _prev.Click += (_, _) => GoToPage(_doc.Page - 1);
        _next.Click += (_, _) => GoToPage(_doc.Page + 1);
        _addPage.Click += (_, _) => AddPage();
        _deletePage.Click += async (_, _) => await DeletePageAsync();
        tools.Children.Add(_prev);
        tools.Children.Add(_pageText);
        tools.Children.Add(_next);
        tools.Children.Add(_addPage);
        tools.Children.Add(_deletePage);

        _size.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _loading) return;
            var v = SketchOps.ClampWidth(Math.Round(_size.Value));
            if (_canvas.Tool == SketchTool.Eraser) _doc.EraserWidth = v; else _doc.PenWidth = v;
            _sizeText.Text = v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            _canvas.InvalidateVisual();
            ScheduleSave();
        };
        _sharp.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _loading) return;
            _doc.Sharpness = SketchOps.ClampSharpness((int)Math.Round(_sharp.Value));
            _sharpText.Text = _doc.Sharpness.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ScheduleSave();
        };

        var toolBar = new Border { Background = Bar, Padding = new Thickness(8, 4), Child = tools };
        DockPanel.SetDock(toolBar, Dock.Top);
        var statusBar = new Border { Padding = new Thickness(10, 3), Child = _status };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        var paper = new Border
        {
            Margin = new Thickness(8, 6, 8, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#E4D08A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = _canvas
        };
        Content = new DockPanel { Children = { toolBar, statusBar, paper } };

        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += (_, _) => SaveNow();
        Closed += (_, _) => _saveTimer.Stop();

        _loading = true;
        _lined.IsChecked = _doc.Lined;
        _sharp.Value = _doc.Sharpness;
        _sharpText.Text = _doc.Sharpness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _loading = false;
        SetColor(_doc.PenColor, save: false);
        SetTool(SketchTool.Pen);
        UpdatePage();
        if (warning != null) Say(Localizer.T("sketch.load_bad", warning), true);
        else ShowHint();
        AppLog.Write("sketch", $"opened: {_doc.Pages.Count} page(s), {_doc.TotalPoints} point(s)");
    }

    // ── test seams ──
    public SketchDocument Document => _doc;
    public SketchCanvas Canvas => _canvas;
    public string StatusText => _status.Text ?? "";
    public bool CanUndo => _undoSteps.Count > 0;
    public bool CanRedo => _redoSteps.Count > 0;
    public void SetSize(double value) => _size.Value = value;
    public void SetSharpness(int value) => _sharp.Value = value;
    public void SetHex(string text) { _hex.Text = text; ApplyHex(); }
    public void ClearAllNow() => ClearAll();

    public void SetTool(SketchTool tool)
    {
        _canvas.Tool = tool;
        foreach (var (t, b) in _toolButtons)
        {
            b.IsChecked = t == tool;
            b.Background = t == tool ? ToolOn : ToolBg;
            b.Foreground = t == tool ? Brushes.White : ToolFg;
        }
        _loading = true;
        _sizeLabel.Text = Localizer.T(tool == SketchTool.Eraser ? "sketch.eraser_size" : "sketch.size");
        _size.Value = tool == SketchTool.Eraser ? _doc.EraserWidth : _doc.PenWidth;
        _sizeText.Text = _size.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        _loading = false;
        _sharp.IsEnabled = tool != SketchTool.Eraser;
        if (!_canvas.IsDrawing) ShowHint();
    }

    public void SetColor(string color, bool save = true)
    {
        var c = SketchOps.NormalizeColor(color);
        _doc.PenColor = c;
        _hex.Text = c;
        _currentColor.Background = new SolidColorBrush(Color.Parse(c));
        foreach (var (sc, swatch) in _swatches)
        {
            var picked = string.Equals(sc, c, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(picked ? 3 : 1);
            swatch.BorderBrush = picked ? SwatchPicked : SwatchEdge;
        }
        if (_canvas.Tool == SketchTool.Eraser) SetTool(SketchTool.Pen); // picking a colour means drawing again
        if (save) ScheduleSave();
    }

    private void ApplyHex()
    {
        if (SketchOps.ParseColor(_hex.Text) is { } c) SetColor(c);
        else
        {
            Say(Localizer.T("sketch.hex_bad"), true);
            _hex.Text = _doc.PenColor;
        }
    }

    private void PushUndo()
    {
        _undoSteps.Add(Snapshot());
        if (_undoSteps.Count > MaxUndo) _undoSteps.RemoveAt(0);
        _redoSteps.Clear();
        UpdateUndoButtons();
    }

    private (List<List<SketchStroke>>, int) Snapshot() => (_doc.Pages.Select(p => p.Strokes.ToList()).ToList(), _doc.Page);

    private void Restore((List<List<SketchStroke>> Pages, int Page) step)
    {
        _doc.Pages = step.Pages.Select(s => new SketchPage { Strokes = s.ToList() }).ToList();
        if (_doc.Pages.Count == 0) _doc.Pages.Add(new SketchPage());
        _doc.Page = Math.Clamp(step.Page, 0, _doc.Pages.Count - 1);
        _canvas.Refresh();
        UpdatePage();
        ScheduleSave();
    }

    public void Undo()
    {
        if (_canvas.IsDrawing || _undoSteps.Count == 0) return;
        _redoSteps.Add(Snapshot());
        var step = _undoSteps[^1];
        _undoSteps.RemoveAt(_undoSteps.Count - 1);
        Restore(step);
        Say(Localizer.T("sketch.undone"));
    }

    public void Redo()
    {
        if (_canvas.IsDrawing || _redoSteps.Count == 0) return;
        _undoSteps.Add(Snapshot());
        var step = _redoSteps[^1];
        _redoSteps.RemoveAt(_redoSteps.Count - 1);
        Restore(step);
        Say(Localizer.T("sketch.redone"));
    }

    private void UpdateUndoButtons()
    {
        _undo.IsEnabled = _undoSteps.Count > 0;
        _redo.IsEnabled = _redoSteps.Count > 0;
    }

    private async Task ClearAllAsync()
    {
        if (_asking || _doc.Current.Strokes.Count == 0) { Say(Localizer.T("sketch.nothing_to_clear")); return; }
        _asking = true;
        try
        {
            var yes = await MessageBox.Show(this, Localizer.T("sketch.clear_confirm", _doc.Page + 1), Localizer.T("sketch.title"),
                Localizer.T("sketch.clear_yes"), Localizer.T("common.cancel"));
            if (yes) ClearAll();
        }
        finally { _asking = false; }
    }

    private void ClearAll()
    {
        if (_doc.Current.Strokes.Count == 0) return;
        PushUndo();
        _doc.Current.Strokes = [];
        _canvas.Refresh();
        ScheduleSave();
        Say(Localizer.T("sketch.cleared"));
    }

    public void GoToPage(int page)
    {
        if (_canvas.IsDrawing || page < 0 || page >= _doc.Pages.Count || page == _doc.Page) return;
        _doc.Page = page;
        _canvas.Refresh();
        UpdatePage();
        ScheduleSave();
    }

    public void AddPage()
    {
        if (_canvas.IsDrawing) return;
        if (_doc.Pages.Count >= SketchOps.MaxPages) { Say(Localizer.T("sketch.max_pages", SketchOps.MaxPages), true); return; }
        PushUndo();
        _doc.Pages.Insert(_doc.Page + 1, new SketchPage());
        _doc.Page++;
        _canvas.Refresh();
        UpdatePage();
        ScheduleSave();
    }

    private async Task DeletePageAsync()
    {
        if (_asking || _canvas.IsDrawing) return;
        if (_doc.Current.Strokes.Count > 0)
        {
            _asking = true;
            try
            {
                if (!await MessageBox.Show(this, Localizer.T("sketch.delete_page_confirm", _doc.Page + 1), Localizer.T("sketch.title"),
                        Localizer.T("sketch.delete_page_yes"), Localizer.T("common.cancel"))) return;
            }
            finally { _asking = false; }
        }
        DeletePage();
    }

    public void DeletePage()
    {
        PushUndo();
        if (_doc.Pages.Count == 1) _doc.Pages[0] = new SketchPage();
        else
        {
            _doc.Pages.RemoveAt(_doc.Page);
            _doc.Page = Math.Min(_doc.Page, _doc.Pages.Count - 1);
        }
        _canvas.Refresh();
        UpdatePage();
        ScheduleSave();
    }

    private void UpdatePage()
    {
        _pageText.Text = Localizer.T("sketch.page", _doc.Page + 1, _doc.Pages.Count);
        _prev.IsEnabled = _doc.Page > 0;
        _next.IsEnabled = _doc.Page < _doc.Pages.Count - 1;
        _addPage.IsEnabled = _doc.Pages.Count < SketchOps.MaxPages;
        UpdateUndoButtons();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (ctrl && e.Key == Key.Z && !shift) { Undo(); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) { Redo(); e.Handled = true; return; }
        if (e.Source is TextBox || ctrl || (e.KeyModifiers & KeyModifiers.Alt) != 0) return;
        switch (e.Key)
        {
            case Key.P: SetTool(SketchTool.Pen); break;
            case Key.E: SetTool(SketchTool.Eraser); break;
            case Key.R: SetTool(SketchTool.Ruler); break;
            case Key.PageUp: GoToPage(_doc.Page - 1); break;
            case Key.PageDown: GoToPage(_doc.Page + 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ScheduleSave()
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes sketch.json now (also when the window closes and when TableNote exits).</summary>
    public bool SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            SketchStore.Save(_file, _doc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("sketch", "save failed: " + ex.Message);
            Say(Localizer.T("sketch.save_failed", ex.Message), true);
            return false;
        }
    }

    private void ShowHint() => Say(Localizer.T(_canvas.Tool switch
    {
        SketchTool.Eraser => "sketch.hint_eraser",
        SketchTool.Ruler => "sketch.hint_ruler",
        _ => "sketch.hint_pen"
    }));

    private void Say(string text, bool warn = false)
    {
        _status.Text = text;
        _status.Foreground = warn ? Warn : Hint;
    }

    private static void StyleTool(ContentControl b)
    {
        b.Background = ToolBg;
        b.Foreground = ToolFg;
        b.Padding = new Thickness(9, 3);
        b.MinHeight = 0;
        b.FontSize = 12;
        b.CornerRadius = new CornerRadius(6);
        b.BorderBrush = new SolidColorBrush(Color.Parse("#D4B86A"));
        b.BorderThickness = new Thickness(1);
        b.Margin = new Thickness(0, 2, 4, 2);
        b.VerticalAlignment = VerticalAlignment.Center;
    }

    private static Border Sep() => new()
    {
        Width = 1, Height = 18, Background = new SolidColorBrush(Color.Parse("#E4D08A")),
        Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
    };
}
