using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SketchTests")]

namespace TableNote;

/// <summary>
/// 2.6.1.1: drawing and pictures on the table, in layers (the Draw button), and pictures in cells. The layers lie over
/// the table and scroll with it; each can be shown or hidden (also from View → Note layers when not drawing). While
/// drawing is on, the bar under the toolbar has the pen, eraser, ruler, move tool, colour, size, sharpness 0–100,
/// undo / redo, Clear all, Insert picture and the layers. Kept in layers.json and the images folder next to note.json.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush InkToolBg = new SolidColorBrush(Color.Parse("#F0D78A"));
    private static readonly IBrush InkToolOn = new SolidColorBrush(Color.Parse("#C08A1E"));
    private static readonly IBrush InkFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush InkSwatchEdge = new SolidColorBrush(Color.Parse("#8A7A55"));
    private static readonly IBrush InkLayerOn = new SolidColorBrush(Color.Parse("#F4D98C"));
    private static readonly IBrush InkLayerEdge = new SolidColorBrush(Color.Parse("#D4B86A"));
    private const int InkMaxUndo = 100;

    private InkDocument _ink = new();
    private string _inkPath = "";
    private readonly InkOverlay _inkOverlay = new();
    private bool _inkOn;
    private ScrollViewer? _rowsScroll;
    private readonly Dictionary<string, Bitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(List<InkLayer> Layers, int Active)> _inkUndo = [];
    private readonly List<(List<InkLayer> Layers, int Active)> _inkRedo = [];
    private DispatcherTimer? _inkSaveTimer;
    private bool _inkLoading;
    private bool _inkAsking;

    private readonly Dictionary<SketchTool, ToggleButton> _inkTools = [];
    private readonly List<(string Color, Border Swatch)> _inkSwatches = [];
    private readonly TextBox _inkHex = new() { Width = 84, FontSize = 12, MinHeight = 0, Height = 26, Padding = new Thickness(5, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _inkCurrent = new() { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _inkSize = new() { Minimum = SketchOps.MinWidth, Maximum = SketchOps.MaxWidth, Width = 100, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inkSizeText = new() { MinWidth = 22, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inkSizeLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Slider _inkSharp = new() { Minimum = 0, Maximum = 100, Width = 110, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inkSharpText = new() { MinWidth = 22, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _inkLayerChips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inkInfo = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#6B5E48")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private Button _inkUndoButton = null!;
    private Button _inkRedoButton = null!;
    private Button _inkDeleteImage = null!;
    private Button _inkAddLayer = null!;

    // ── test seams ──
    internal InkDocument Ink => _ink;
    internal InkOverlay InkLayerControl => _inkOverlay;
    internal bool InkOn => _inkOn;
    internal string InkInfo => _inkInfo.Text ?? "";

    private void InitInk()
    {
        _inkPath = InkStore.PathFor(_path);
        _ink = InkStore.Load(_inkPath, out var warning);
        if (warning != null) ActivityLog.Shared.Add(ActivityLevel.Warning, Localizer.T("ink.load_bad", warning));
        _inkOverlay.Document = _ink;
        _inkOverlay.ImageFor = BitmapFor;
        _inkOverlay.Offset = () => new Vector(TableScroll.Offset.X, RowsScroll()?.Offset.Y ?? 0);
        _inkOverlay.Committing += PushInkUndo;
        _inkOverlay.Changed += () => { ScheduleInkSave(); UpdateInkButtons(); };
        _inkOverlay.Full += () => SayInk(Localizer.T("sketch.full"));
        _inkOverlay.Info += SayInk;
        _inkOverlay.ScrollBy += ScrollTableBy;
        TableLayerHost.Children.Add(_inkOverlay);
        TableLayerHost.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => _inkOverlay.InvalidateVisual(), RoutingStrategies.Bubble);
        _inkSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _inkSaveTimer.Tick += (_, _) => { _inkSaveTimer.Stop(); SaveInkNow(); };
        SketchButton.Click += (_, _) => SetDrawMode(!_inkOn);
        BuildInkBar();
    }

    private void ApplySketchTexts()
    {
        SketchButton.Content = Localizer.T("toolbar.sketch");
        ToolTip.SetTip(SketchButton, Localizer.T("tip.sketch"));
        if (InkBar.Child != null) BuildInkBar();
    }

    private ScrollViewer? RowsScroll() => _rowsScroll ??= RowsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void ScrollTableBy(Vector by)
    {
        if (by.X != 0) TableScroll.Offset = new Vector(Math.Max(0, TableScroll.Offset.X + by.X), TableScroll.Offset.Y);
        if (by.Y != 0 && RowsScroll() is { } sv) sv.Offset = new Vector(sv.Offset.X, Math.Max(0, sv.Offset.Y + by.Y));
        _inkOverlay.InvalidateVisual();
    }

    /// <summary>Drawing on or off. On: the bar shows and the layers take the mouse; off: the table works as usual.</summary>
    internal void SetDrawMode(bool on)
    {
        if (on && _view != ViewMode.Edit) return;
        if (on == _inkOn) return;
        if (on) CommitFocusedEdit();
        _inkOn = on;
        _ = _ink.ActiveLayer; // makes the first layer
        if (on && !_ink.ActiveLayer.Visible) { _ink.ActiveLayer.Visible = true; ScheduleInkSave(); }
        _inkOverlay.DrawMode = on;
        InkBar.IsVisible = on;
        if (on) { SketchButton.Background = InkToolOn; SketchButton.Foreground = Brushes.White; }
        else { SketchButton.ClearValue(BackgroundProperty); SketchButton.ClearValue(ForegroundProperty); }
        if (on)
        {
            SetInkTool(_inkOverlay.Tool);
            RefreshInkBar();
            _inkOverlay.Focus();
            StatusText.Text = Localizer.T("ink.on");
        }
        else
        {
            SaveInkNow();
            StatusText.Text = Localizer.T("ink.off");
        }
    }

    // ───────────────────────── the bar ─────────────────────────

    private void BuildInkBar()
    {
        _inkTools.Clear();
        _inkSwatches.Clear();
        var row1 = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, key) in new[] { (SketchTool.Pen, "sketch.pen"), (SketchTool.Eraser, "sketch.eraser"), (SketchTool.Ruler, "sketch.ruler"), (SketchTool.Move, "ink.move") })
        {
            var t = tool;
            var b = new ToggleButton { Content = Localizer.T(key) };
            InkStyle(b);
            ToolTip.SetTip(b, Localizer.T(key + "_tip"));
            b.Click += (_, _) => SetInkTool(t);
            _inkTools[tool] = b;
            row1.Children.Add(b);
        }
        row1.Children.Add(InkSep());
        row1.Children.Add(InkLabel(Localizer.T("sketch.color")));
        foreach (var color in SketchOps.Palette)
        {
            var c = color;
            var swatch = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(1, 0),
                Background = new SolidColorBrush(Color.Parse(c)), BorderBrush = InkSwatchEdge, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(swatch, c);
            swatch.PointerPressed += (_, e) => { SetInkColor(c); e.Handled = true; };
            _inkSwatches.Add((c, swatch));
            row1.Children.Add(swatch);
        }
        _inkHex.Margin = new Thickness(6, 0, 4, 0);
        ToolTip.SetTip(_inkHex, Localizer.T("sketch.hex_tip"));
        if (_inkHex.Tag == null)
        {
            _inkHex.Tag = "ink";
            _inkHex.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { ApplyInkHex(); _inkOverlay.Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _inkHex.Text = _ink.PenColor; _inkOverlay.Focus(); e.Handled = true; }
            };
            _inkHex.LostFocus += (_, _) => ApplyInkHex();
            _inkSize.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty || _inkLoading) return;
                var v = SketchOps.ClampWidth(Math.Round(_inkSize.Value));
                if (_inkOverlay.Tool == SketchTool.Eraser) _ink.EraserWidth = v; else _ink.PenWidth = v;
                _inkSizeText.Text = v.ToString("0");
                _inkOverlay.InvalidateVisual();
                ScheduleInkSave();
            };
            _inkSharp.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty || _inkLoading) return;
                _ink.Sharpness = SketchOps.ClampSharpness((int)Math.Round(_inkSharp.Value));
                _inkSharpText.Text = _ink.Sharpness.ToString();
                ScheduleInkSave();
            };
        }
        Detach(_inkHex, _inkCurrent, _inkSizeLabel, _inkSize, _inkSizeText, _inkSharp, _inkSharpText, _inkLayerChips, _inkInfo);
        row1.Children.Add(_inkHex);
        row1.Children.Add(_inkCurrent);
        row1.Children.Add(InkSep());
        row1.Children.Add(_inkSizeLabel);
        row1.Children.Add(_inkSize);
        row1.Children.Add(_inkSizeText);
        row1.Children.Add(InkSep());
        var sharpLabel = InkLabel(Localizer.T("sketch.sharpness"));
        ToolTip.SetTip(sharpLabel, Localizer.T("sketch.sharpness_tip"));
        ToolTip.SetTip(_inkSharp, Localizer.T("sketch.sharpness_tip"));
        row1.Children.Add(sharpLabel);
        row1.Children.Add(_inkSharp);
        row1.Children.Add(_inkSharpText);

        var row2 = new WrapPanel { Orientation = Orientation.Horizontal };
        _inkUndoButton = InkButton("↩", "sketch.undo_tip", InkUndo);
        _inkRedoButton = InkButton("↪", "sketch.redo_tip", InkRedo);
        row2.Children.Add(_inkUndoButton);
        row2.Children.Add(_inkRedoButton);
        row2.Children.Add(InkButton(Localizer.T("sketch.clear"), "ink.clear_tip", () => _ = ClearInkLayerAsync()));
        row2.Children.Add(InkSep());
        row2.Children.Add(InkButton(Localizer.T("ink.add_image"), "ink.add_image_tip", () => _ = InsertLayerImageAsync()));
        _inkDeleteImage = InkButton(Localizer.T("ink.delete_image"), "ink.delete_image_tip", DeleteSelectedImage);
        row2.Children.Add(_inkDeleteImage);
        row2.Children.Add(InkSep());
        row2.Children.Add(InkLabel(Localizer.T("ink.layers")));
        row2.Children.Add(_inkLayerChips);
        _inkAddLayer = InkButton(Localizer.T("ink.add_layer"), "ink.add_layer_tip", AddInkLayer);
        row2.Children.Add(_inkAddLayer);
        row2.Children.Add(InkButton(Localizer.T("ink.rename_layer"), "ink.rename_layer_tip", () => _ = RenameInkLayerAsync()));
        row2.Children.Add(InkButton(Localizer.T("ink.delete_layer"), "ink.delete_layer_tip", () => _ = DeleteInkLayerAsync()));
        row2.Children.Add(InkSep());
        var done = InkButton(Localizer.T("ink.done"), "ink.done_tip", () => SetDrawMode(false));
        done.Background = InkToolOn;
        done.Foreground = Brushes.White;
        row2.Children.Add(done);
        row2.Children.Add(_inkInfo);

        InkBar.Child = new StackPanel { Spacing = 2, Children = { row1, row2 } };
        RefreshInkBar();
    }

    private static void Detach(params Control[] controls)
    {
        foreach (var c in controls)
            if (c.Parent is Panel p) p.Children.Remove(c);
    }

    private void RefreshInkBar()
    {
        _inkLoading = true;
        _inkSharp.Value = _ink.Sharpness;
        _inkSharpText.Text = _ink.Sharpness.ToString();
        _inkLoading = false;
        SetInkColor(_ink.PenColor, save: false, keepTool: true);
        SetInkTool(_inkOverlay.Tool);
        RefreshLayerChips();
        UpdateInkButtons();
    }

    internal void SetInkTool(SketchTool tool)
    {
        _inkOverlay.Tool = tool;
        foreach (var (t, b) in _inkTools)
        {
            b.IsChecked = t == tool;
            b.Background = t == tool ? InkToolOn : InkToolBg;
            b.Foreground = t == tool ? Brushes.White : InkFg;
        }
        _inkLoading = true;
        _inkSizeLabel.Text = Localizer.T(tool == SketchTool.Eraser ? "sketch.eraser_size" : "sketch.size");
        _inkSize.Value = tool == SketchTool.Eraser ? _ink.EraserWidth : _ink.PenWidth;
        _inkSizeText.Text = _inkSize.Value.ToString("0");
        _inkLoading = false;
        _inkSharp.IsEnabled = tool is SketchTool.Pen or SketchTool.Ruler;
        _inkSize.IsEnabled = tool != SketchTool.Move;
        SayInk(Localizer.T(tool switch
        {
            SketchTool.Eraser => "sketch.hint_eraser",
            SketchTool.Ruler => "sketch.hint_ruler",
            SketchTool.Move => "ink.hint_move",
            _ => "ink.hint_pen"
        }));
        UpdateInkButtons();
    }

    internal void SetInkColor(string color, bool save = true, bool keepTool = false)
    {
        var c = SketchOps.NormalizeColor(color);
        _ink.PenColor = c;
        _inkHex.Text = c;
        _inkCurrent.Background = new SolidColorBrush(Color.Parse(c));
        _inkCurrent.BorderBrush = InkSwatchEdge;
        foreach (var (sc, swatch) in _inkSwatches)
        {
            var picked = string.Equals(sc, c, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(picked ? 3 : 1);
            swatch.BorderBrush = picked ? InkFg : InkSwatchEdge;
        }
        if (!keepTool && _inkOverlay.Tool is SketchTool.Eraser or SketchTool.Move) SetInkTool(SketchTool.Pen);
        if (save) ScheduleInkSave();
    }

    internal void SetInkHex(string text) { _inkHex.Text = text; ApplyInkHex(); }
    internal void SetInkSize(double v) => _inkSize.Value = v;
    internal void SetInkSharpness(int v) => _inkSharp.Value = v;

    private void ApplyInkHex()
    {
        if (SketchOps.ParseColor(_inkHex.Text) is { } c) { if (c != _ink.PenColor) SetInkColor(c); }
        else { SayInk(Localizer.T("sketch.hex_bad")); _inkHex.Text = _ink.PenColor; }
    }

    private void RefreshLayerChips()
    {
        _inkLayerChips.Children.Clear();
        for (var i = 0; i < _ink.Layers.Count; i++)
        {
            var index = i;
            var layer = _ink.Layers[i];
            var eye = new CheckBox { IsChecked = layer.Visible, MinHeight = 0, MinWidth = 0, Padding = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(eye, Localizer.T("ink.layer_visible_tip"));
            eye.IsCheckedChanged += (_, _) => SetLayerVisible(index, eye.IsChecked == true);
            var name = new TextBlock { Text = layer.Name, FontSize = 12, Foreground = InkFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var chip = new Border
            {
                Background = index == _ink.Active ? InkLayerOn : Brushes.Transparent,
                BorderBrush = index == _ink.Active ? InkToolOn : InkLayerEdge,
                BorderThickness = new Thickness(index == _ink.Active ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 0),
                Margin = new Thickness(0, 2, 4, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "layer",
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { eye, name } }
            };
            ToolTip.SetTip(chip, Localizer.T("ink.layer_pick_tip"));
            name.PointerPressed += (_, e) => { SelectInkLayer(index); e.Handled = true; };
            _inkLayerChips.Children.Add(chip);
        }
        if (_inkAddLayer != null) _inkAddLayer.IsEnabled = _ink.Layers.Count < InkOps.MaxLayers;
    }

    internal void SelectInkLayer(int index)
    {
        if (index < 0 || index >= _ink.Layers.Count) return;
        _ink.Active = index;
        _inkOverlay.Selected = null;
        if (!_ink.Layers[index].Visible) SayInk(Localizer.T("ink.layer_hidden"));
        RefreshLayerChips();
        _inkOverlay.InvalidateVisual();
        ScheduleInkSave();
    }

    internal void SetLayerVisible(int index, bool visible)
    {
        if (index < 0 || index >= _ink.Layers.Count || _ink.Layers[index].Visible == visible) return;
        _ink.Layers[index].Visible = visible;
        if (!visible && index == _ink.Active) _inkOverlay.Selected = null;
        _inkOverlay.InvalidateVisual();
        if (InkBar.IsVisible) RefreshLayerChips();
        ScheduleInkSave();
        AppLog.Write("ink", $"layer {index + 1} {(visible ? "shown" : "hidden")}");
    }

    internal void AddInkLayer()
    {
        PushInkUndo();
        if (InkOps.AddLayer(_ink) == null) { _inkUndo.RemoveAt(_inkUndo.Count - 1); SayInk(Localizer.T("ink.max_layers", InkOps.MaxLayers)); return; }
        AfterInkStructure();
    }

    private async Task DeleteInkLayerAsync()
    {
        if (_inkAsking) return;
        var layer = _ink.ActiveLayer;
        if (layer.Strokes.Count > 0 || layer.Images.Count > 0)
        {
            _inkAsking = true;
            try
            {
                if (!await MessageBox.Show(this, Localizer.T("ink.delete_layer_confirm", layer.Name), Localizer.T("ink.title"),
                        Localizer.T("ink.delete_layer"), Localizer.T("common.cancel"))) return;
            }
            finally { _inkAsking = false; }
        }
        DeleteInkLayer();
    }

    internal void DeleteInkLayer()
    {
        PushInkUndo();
        InkOps.DeleteActiveLayer(_ink);
        AfterInkStructure();
    }

    private async Task RenameInkLayerAsync()
    {
        if (_inkAsking) return;
        _inkAsking = true;
        try
        {
            var name = await AskText(Localizer.T("ink.rename_layer"), Localizer.T("ink.rename_prompt"), _ink.ActiveLayer.Name);
            if (name != null) RenameInkLayer(name);
        }
        finally { _inkAsking = false; }
    }

    internal void RenameInkLayer(string name)
    {
        var clean = InkOps.CleanName(name);
        if (clean.Length == 0 || clean == _ink.ActiveLayer.Name) return;
        PushInkUndo();
        _ink.ActiveLayer.Name = clean;
        AfterInkStructure();
    }

    private void AfterInkStructure()
    {
        _inkOverlay.Refresh();
        RefreshLayerChips();
        UpdateInkButtons();
        ScheduleInkSave();
    }

    private async Task ClearInkLayerAsync()
    {
        var layer = _ink.ActiveLayer;
        if (layer.Strokes.Count == 0 && layer.Images.Count == 0) { SayInk(Localizer.T("ink.nothing_to_clear")); return; }
        if (_inkAsking) return;
        _inkAsking = true;
        try
        {
            if (await MessageBox.Show(this, Localizer.T("ink.clear_confirm", layer.Name), Localizer.T("ink.title"),
                    Localizer.T("sketch.clear_yes"), Localizer.T("common.cancel")))
                ClearInkLayer();
        }
        finally { _inkAsking = false; }
    }

    internal void ClearInkLayer()
    {
        var layer = _ink.ActiveLayer;
        if (layer.Strokes.Count == 0 && layer.Images.Count == 0) return;
        PushInkUndo();
        layer.Strokes = [];
        layer.Images = [];
        AfterInkStructure();
        SayInk(Localizer.T("ink.cleared"));
    }

    // ───────────────────────── undo ─────────────────────────

    private void PushInkUndo()
    {
        _inkUndo.Add((_ink.Layers.Select(l => l.Clone()).ToList(), _ink.Active));
        if (_inkUndo.Count > InkMaxUndo) _inkUndo.RemoveAt(0);
        _inkRedo.Clear();
        UpdateInkButtons();
    }

    private void RestoreInk((List<InkLayer> Layers, int Active) step)
    {
        // Layer visibility is a view setting, not an undo step: keep what is shown now.
        var visible = _ink.Layers.ToDictionary(l => l.Id, l => l.Visible);
        _ink.Layers = step.Layers.Select(l => l.Clone()).ToList();
        foreach (var l in _ink.Layers) if (visible.TryGetValue(l.Id, out var v)) l.Visible = v;
        _ink.Active = step.Active;
        AfterInkStructure();
    }

    internal void InkUndo()
    {
        if (_inkOverlay.IsDrawing || _inkUndo.Count == 0) return;
        _inkRedo.Add((_ink.Layers.Select(l => l.Clone()).ToList(), _ink.Active));
        var step = _inkUndo[^1];
        _inkUndo.RemoveAt(_inkUndo.Count - 1);
        RestoreInk(step);
        SayInk(Localizer.T("sketch.undone"));
    }

    internal void InkRedo()
    {
        if (_inkOverlay.IsDrawing || _inkRedo.Count == 0) return;
        _inkUndo.Add((_ink.Layers.Select(l => l.Clone()).ToList(), _ink.Active));
        var step = _inkRedo[^1];
        _inkRedo.RemoveAt(_inkRedo.Count - 1);
        RestoreInk(step);
        SayInk(Localizer.T("sketch.redone"));
    }

    private void UpdateInkButtons()
    {
        if (_inkUndoButton == null) return;
        _inkUndoButton.IsEnabled = _inkUndo.Count > 0;
        _inkRedoButton.IsEnabled = _inkRedo.Count > 0;
        _inkDeleteImage.IsEnabled = _inkOverlay.Selected != null;
    }

    /// <summary>Keys while drawing: Ctrl+Z / Ctrl+Y, Esc (done), Delete (the picked picture), P / E / R / M.</summary>
    private bool HandleInkKey(KeyEventArgs e)
    {
        if (!_inkOn) return false;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && e.Key == Key.Z && !shift) { InkUndo(); e.Handled = true; return true; }
        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) { InkRedo(); e.Handled = true; return true; }
        if (e.Source is TextBox || ctrl || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false;
        switch (e.Key)
        {
            case Key.Escape: SetDrawMode(false); break;
            case Key.Delete or Key.Back: DeleteSelectedImage(); break;
            case Key.P: SetInkTool(SketchTool.Pen); break;
            case Key.E: SetInkTool(SketchTool.Eraser); break;
            case Key.R: SetInkTool(SketchTool.Ruler); break;
            case Key.M: SetInkTool(SketchTool.Move); break;
            default: return false;
        }
        e.Handled = true;
        return true;
    }

    // ───────────────────────── pictures on a layer ─────────────────────────

    private async Task InsertLayerImageAsync()
    {
        if (await PickImageAsync() is not { } path) return;
        InsertLayerImage(path);
    }

    /// <summary>Copies the picture into the images folder and puts it on the active layer, at the top left of the view.</summary>
    internal bool InsertLayerImage(string path)
    {
        if (_ink.TotalImages >= InkOps.MaxImages) { SayInk(Localizer.T("ink.max_images", InkOps.MaxImages)); return false; }
        string file;
        try { file = InkStore.Import(_path, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SayInk(Localizer.T("ink.image_failed", ex.Message));
            AppLog.Write("ink", "insert picture failed: " + ex.Message);
            return false;
        }
        var bmp = BitmapFor(file);
        if (bmp == null) { SayInk(Localizer.T("ink.image_failed", Path.GetFileName(path))); return false; }
        var (w, h) = InkOps.PlaceSizeFor(bmp.Size.Width, bmp.Size.Height);
        var at = _inkOverlay.Offset() + new Vector(40, 40);
        var img = new SketchImage(InkOps.NewId(), file, Math.Round(at.X), Math.Round(at.Y), Math.Round(w, 1), Math.Round(h, 1));
        PushInkUndo();
        var layer = _ink.ActiveLayer;
        layer.Visible = true;
        layer.Images.Add(img);
        SetInkTool(SketchTool.Move);
        _inkOverlay.Selected = img.Id;
        AfterInkStructure();
        SayInk(Localizer.T("ink.image_added"));
        AppLog.Write("ink", $"picture {file} on layer {_ink.Active + 1}");
        return true;
    }

    internal void DeleteSelectedImage()
    {
        var layer = _ink.ActiveLayer;
        var index = layer.Images.FindIndex(i => i.Id == _inkOverlay.Selected);
        if (index < 0) { SayInk(Localizer.T("ink.pick_image_first")); return; }
        PushInkUndo();
        layer.Images.RemoveAt(index);
        _inkOverlay.Selected = null;
        AfterInkStructure();
    }

    private async Task<string?> PickImageAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } sp) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("ink.pick_title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Localizer.T("ink.image_files")) { Patterns = InkStore.ImageExtensions.Select(x => "*" + x).ToList() }]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>A picture from the images folder, read once (large pictures are kept at most 1600 px wide).</summary>
    private Bitmap? BitmapFor(string file)
    {
        if (_bitmaps.TryGetValue(file, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            var path = InkStore.ImagePath(_path, file);
            if (File.Exists(path))
            {
                bmp = new Bitmap(path);
                if (bmp.PixelSize.Width > 1600)
                {
                    bmp.Dispose();
                    using var s = File.OpenRead(path);
                    bmp = Bitmap.DecodeToWidth(s, 1600);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("ink", "could not read picture " + file + ": " + ex.Message);
            bmp = null;
        }
        _bitmaps[file] = bmp;
        return bmp;
    }

    // ───────────────────────── pictures in cells ─────────────────────────

    private CellImage? CellImageAt(int row, int column) =>
        row >= 0 && column >= 0 && CellDetailOps.TryKey(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column), out var key) &&
        _ink.CellImages.TryGetValue(key, out var img) ? img : null;

    /// <summary>A cell with a picture gets a toggle on its right; open, the picture is shown under the text.</summary>
    private Control AttachCellImage(int row, int column, Control inner, IBrush bg)
    {
        if (CellImageAt(row, column) is not { } img) return inner;
        var toggle = new Button
        {
            Name = "CellImageToggle",
            Content = Localizer.T(img.Open ? "cellimg.hide" : "cellimg.show"),
            FontFamily = AppFonts.Thai,
            FontSize = 11,
            Background = img.Open ? DetailButtonOn : DetailButtonBg,
            Foreground = DetailButtonFg,
            BorderBrush = DetailButtonBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(2),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ToolTip.SetTip(toggle, Localizer.T(img.Open ? "cellimg.hide_tip" : "cellimg.show_tip"));
        toggle.Click += (_, e) => { ToggleCellImage(row, column); e.Handled = true; };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(toggle, 1);
        top.Children.Add(inner);
        top.Children.Add(toggle);
        if (!img.Open) return top;

        var bmp = BitmapFor(img.File);
        Control picture = bmp != null
            ? new Image { Source = bmp, Stretch = Stretch.Uniform, MaxHeight = 180, HorizontalAlignment = HorizontalAlignment.Left, Cursor = new Cursor(StandardCursorType.Hand) }
            : new TextBlock { Text = Localizer.T("cellimg.missing", img.File), FontSize = 11, Foreground = DetailButtonFg, TextWrapping = TextWrapping.Wrap };
        picture.Name = "CellImage";
        if (bmp != null)
        {
            ToolTip.SetTip(picture, Localizer.T("cellimg.view_tip"));
            picture.PointerPressed += (_, e) => { if (e.GetCurrentPoint(picture).Properties.IsLeftButtonPressed) { ShowPicture(img.File); e.Handled = true; } };
        }
        var holder = new Border { Background = bg, Padding = new Thickness(4, 2, 4, 4), Child = picture };
        Grid.SetRow(holder, 1);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.Children.Add(top);
        grid.Children.Add(holder);
        return grid;
    }

    internal void ToggleCellImage(int row, int column)
    {
        if (!CellDetailOps.TryKey(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column), out var key) ||
            !_ink.CellImages.TryGetValue(key, out var img)) return;
        _ink.CellImages[key] = img with { Open = !img.Open };
        ScheduleInkSave();
        RefreshRow(row);
    }

    internal bool SetCellImage(int row, int column, string path)
    {
        if (!CellDetailOps.TryKey(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column), out var key)) return false;
        string file;
        try { file = InkStore.Import(_path, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = Localizer.T("ink.image_failed", ex.Message);
            AppLog.Write("ink", "cell picture failed: " + ex.Message);
            return false;
        }
        if (BitmapFor(file) == null) { StatusText.Text = Localizer.T("ink.image_failed", Path.GetFileName(path)); return false; }
        _ink.CellImages[key] = new CellImage(file, Open: true);
        SaveInkNow();
        RefreshRow(row);
        StatusText.Text = Localizer.T("cellimg.added", ColumnLetter(column) + (row + 1));
        AppLog.Write("ink", $"cell picture {file} in {ColumnLetter(column)}{row + 1}");
        return true;
    }

    internal void RemoveCellImage(int row, int column)
    {
        if (!CellDetailOps.TryKey(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column), out var key) || !_ink.CellImages.Remove(key)) return;
        SaveInkNow();
        RefreshRow(row);
        StatusText.Text = Localizer.T("cellimg.removed", ColumnLetter(column) + (row + 1));
    }

    private async Task PickCellImageAsync(int row, int column)
    {
        if (await PickImageAsync() is { } path) SetCellImage(row, column, path);
    }

    private async Task AskRemoveCellImageAsync(int row, int column)
    {
        if (await MessageBox.Show(this, Localizer.T("cellimg.remove_confirm", ColumnLetter(column) + (row + 1)), Localizer.T("cellimg.title"),
                Localizer.T("cellimg.remove"), Localizer.T("common.cancel")))
            RemoveCellImage(row, column);
    }

    /// <summary>Cell menu items: Insert picture… / Change picture…, Show / Hide picture, Remove picture.</summary>
    private IEnumerable<Control> CellImageMenuItems(int row, int column)
    {
        var edit = _view == ViewMode.Edit && row >= 0 && column >= 0;
        var img = CellImageAt(row, column);
        var add = new MenuItem { Header = Localizer.T(img == null ? "cellimg.add" : "cellimg.change"), IsEnabled = edit, FontFamily = AppFonts.Thai };
        add.Click += (_, _) => _ = PickCellImageAsync(row, column);
        yield return add;
        if (img == null) yield break;
        var toggle = new MenuItem { Header = Localizer.T(img.Open ? "cellimg.hide_menu" : "cellimg.show_menu"), IsEnabled = edit, FontFamily = AppFonts.Thai };
        toggle.Click += (_, _) => ToggleCellImage(row, column);
        yield return toggle;
        var remove = new MenuItem { Header = Localizer.T("cellimg.remove") + "…", IsEnabled = edit, FontFamily = AppFonts.Thai };
        remove.Click += (_, _) => _ = AskRemoveCellImageAsync(row, column);
        yield return remove;
    }

    /// <summary>The whole picture in a window of its own (click a picture in a cell).</summary>
    private void ShowPicture(string file)
    {
        if (BitmapFor(file) is not { } bmp) return;
        var screen = Screens.ScreenFromVisual(this)?.WorkingArea.Size.ToSize(1) ?? new Size(1200, 800);
        var w = Math.Min(bmp.Size.Width + 40, screen.Width * 0.85);
        var h = Math.Min(bmp.Size.Height + 60, screen.Height * 0.85);
        var win = new Window
        {
            Title = Localizer.T("cellimg.view_title"),
            Width = Math.Max(320, w), Height = Math.Max(240, h),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            FontFamily = AppFonts.Thai,
            Icon = Icon,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = bmp, Stretch = Stretch.None, Margin = new Thickness(12) }
            }
        };
        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) win.Close(); };
        win.Show(this);
    }

    // ───────────────────────── View → Note layers ─────────────────────────

    private void FillInkLayerMenu(MenuItem menu)
    {
        menu.Items.Clear();
        _ = _ink.ActiveLayer;
        for (var i = 0; i < _ink.Layers.Count; i++)
        {
            var index = i;
            var layer = _ink.Layers[i];
            var item = new MenuItem { Header = (layer.Visible ? "✓ " : "") + layer.Name, FontFamily = AppFonts.Thai };
            item.Click += (_, _) => SetLayerVisible(index, !_ink.Layers[index].Visible);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = Localizer.T("ink.show_all"), FontFamily = AppFonts.Thai };
        all.Click += (_, _) => { for (var i = 0; i < _ink.Layers.Count; i++) SetLayerVisible(i, true); };
        var none = new MenuItem { Header = Localizer.T("ink.hide_all"), FontFamily = AppFonts.Thai };
        none.Click += (_, _) => { for (var i = 0; i < _ink.Layers.Count; i++) SetLayerVisible(i, false); };
        menu.Items.Add(all);
        menu.Items.Add(none);
    }

    // ───────────────────────── saving ─────────────────────────

    private void ScheduleInkSave()
    {
        if (_inkSaveTimer == null) return;
        _inkSaveTimer.Stop();
        _inkSaveTimer.Start();
    }

    /// <summary>Writes layers.json now (also when drawing ends and when TableNote closes).</summary>
    internal bool SaveInkNow()
    {
        _inkSaveTimer?.Stop();
        if (_inkPath.Length == 0) return true;
        if (!_ink.HasInk && _ink.CellImages.Count == 0 && !File.Exists(_inkPath) && _ink.Layers.Count <= 1) return true; // nothing to keep yet
        try
        {
            InkStore.Save(_inkPath, _ink);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("ink", "save failed: " + ex.Message);
            StatusText.Text = Localizer.T("sketch.save_failed", ex.Message);
            return false;
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    private void SayInk(string text) => _inkInfo.Text = text;

    private Button InkButton(string content, string tipKey, Action act)
    {
        var b = new Button { Content = content };
        InkStyle(b);
        ToolTip.SetTip(b, Localizer.T(tipKey));
        b.Click += (_, _) => act();
        return b;
    }

    private static void InkStyle(ContentControl b)
    {
        b.Background = InkToolBg;
        b.Foreground = InkFg;
        b.Padding = new Thickness(8, 2);
        b.MinHeight = 0;
        b.FontSize = 12;
        b.CornerRadius = new CornerRadius(6);
        b.BorderBrush = InkLayerEdge;
        b.BorderThickness = new Thickness(1);
        b.Margin = new Thickness(0, 2, 4, 2);
        b.VerticalAlignment = VerticalAlignment.Center;
    }

    private static TextBlock InkLabel(string text) => new() { Text = text, FontSize = 12, Foreground = InkFg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    private static Border InkSep() => new()
    {
        Width = 1, Height = 18, Background = new SolidColorBrush(Color.Parse("#E4D08A")),
        Margin = new Thickness(3, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>One line of text in a small window; null when cancelled.</summary>
    private async Task<string?> AskText(string title, string prompt, string value)
    {
        var box = new TextBox { Text = value, FontFamily = AppFonts.Thai, MaxLength = InkOps.MaxLayerName };
        var ok = new Button { Content = Localizer.T("common.save"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = Localizer.T("common.cancel"), IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 12, 0, 0) };
        var win = new Window
        {
            Title = title, Width = 360, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = AppFonts.Thai,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 6,
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            }
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        win.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await win.ShowDialog(this);
        return result;
    }
}
