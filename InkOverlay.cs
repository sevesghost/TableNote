using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TableNote;

/// <summary>
/// 2.6.1.1: the layers drawn over the table. Always shows the visible layers (strokes and pictures) and moves with
/// the table when it scrolls; takes the pen, eraser, ruler and move tools only while drawing is on (the Draw button),
/// so the table works as usual otherwise. Coordinates are table coordinates: the point on screen plus the scroll offset.
/// </summary>
public sealed class InkOverlay : Control
{
    private const double MinStep = 0.8;
    private static readonly IPen ModeEdge = new Pen(new SolidColorBrush(Color.FromArgb(170, 192, 138, 30)), 2);
    private static readonly IPen EraserRing = new Pen(new SolidColorBrush(Color.Parse("#8A7A55")), 1, new DashStyle([3, 2], 0));
    private static readonly IBrush EraserFill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 192, 138, 30)), 1, new DashStyle([4, 3], 0));
    private static readonly IPen SelectPen = new Pen(new SolidColorBrush(Color.Parse("#C08A1E")), 1.5, new DashStyle([4, 3], 0));
    private static readonly IBrush HandleFill = new SolidColorBrush(Color.Parse("#C08A1E"));
    private static readonly IBrush MissingFill = new SolidColorBrush(Color.FromArgb(60, 160, 140, 100));
    private static readonly IBrush LabelFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush LabelBg = new SolidColorBrush(Color.FromArgb(225, 255, 248, 224));

    private readonly Dictionary<SketchStroke, Geometry> _geometry = new(ReferenceEqualityComparer.Instance);
    private readonly List<double> _live = [];
    private bool _drawing;
    private bool _changed;
    private SketchTool _gestureTool;
    private Point _start;
    private Point _last;
    private Point? _hover;
    private bool _snap;
    private SketchImage? _dragImage;
    private bool _resizing;
    private Point _grab;

    public InkOverlay()
    {
        ClipToBounds = true;
        Focusable = true;
        IsHitTestVisible = false;
    }

    public InkDocument Document { get; set; } = new();
    /// <summary>The table's scroll offset (horizontal of the table, vertical of the rows).</summary>
    public Func<Vector> Offset { get; set; } = () => default;
    /// <summary>Picture for a file name in the images folder (cached by the caller); null when it can't be read.</summary>
    public Func<string, Bitmap?> ImageFor { get; set; } = _ => null;

    private bool _drawMode;
    /// <summary>Drawing is on: the overlay takes the mouse (the table below does not).</summary>
    public bool DrawMode
    {
        get => _drawMode;
        set { _drawMode = value; IsHitTestVisible = value; if (!value) { Selected = null; _hover = null; } InvalidateVisual(); }
    }

    private SketchTool _tool = SketchTool.Pen;
    public SketchTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            if (value != SketchTool.Move) Selected = null;
            Cursor = new Cursor(value switch
            {
                SketchTool.Eraser => StandardCursorType.None,
                SketchTool.Move => StandardCursorType.Arrow,
                _ => StandardCursorType.Cross
            });
            InvalidateVisual();
        }
    }

    /// <summary>The picture picked with the move tool (on the active layer), or null.</summary>
    public string? Selected { get; set; }

    public bool IsDrawing => _drawing;

    /// <summary>Called once per gesture, just before the layers change (an undo step).</summary>
    public event Action? Committing;
    /// <summary>The layers changed (after a stroke, an erase, a move or resize).</summary>
    public event Action? Changed;
    /// <summary>A stroke was not kept because the layers are full.</summary>
    public event Action? Full;
    /// <summary>The wheel turned while drawing: scroll the table by this many pixels (x, y).</summary>
    public event Action<Vector>? ScrollBy;
    /// <summary>Something to tell the user (ruler length and angle while dragging; empty when done).</summary>
    public event Action<string>? Info;

    public void Refresh()
    {
        var alive = new HashSet<SketchStroke>(Document.Layers.SelectMany(l => l.Strokes), ReferenceEqualityComparer.Instance);
        foreach (var gone in _geometry.Keys.Where(k => !alive.Contains(k)).ToList()) _geometry.Remove(gone);
        if (Selected != null && Document.ActiveLayer.Images.All(i => i.Id != Selected)) Selected = null;
        InvalidateVisual();
    }

    private Point ToContent(Point local) => local + Offset();
    private double EraserRadius => Document.EraserWidth / 2;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!DrawMode) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsEraser) return;
        Focus();
        e.Handled = true;
        var layer = Document.ActiveLayer;
        if (!layer.Visible && Tool != SketchTool.Move) { Info?.Invoke(Localizer.T("ink.layer_hidden")); return; }
        e.Pointer.Capture(this);
        _drawing = true;
        _changed = false;
        _gestureTool = point.Properties.IsEraser ? SketchTool.Eraser : Tool;
        _start = _last = ToContent(point.Position);
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        _live.Clear();
        switch (_gestureTool)
        {
            case SketchTool.Eraser: EraseTo(_start); break;
            case SketchTool.Move: StartMove(layer); break;
            default: _live.Add(_start.X); _live.Add(_start.Y); break;
        }
        InvalidateVisual();
    }

    private void StartMove(InkLayer layer)
    {
        _dragImage = null;
        _resizing = false;
        var selected = layer.Images.FirstOrDefault(i => i.Id == Selected);
        if (selected != null && InkOps.OnHandle(selected, _start.X, _start.Y))
        {
            _dragImage = selected;
            _resizing = true;
        }
        else
        {
            var hit = InkOps.HitImage(layer, _start.X, _start.Y);
            Selected = hit >= 0 ? layer.Images[hit].Id : null;
            if (hit >= 0) _dragImage = layer.Images[hit];
        }
        if (_dragImage != null) _grab = new Point(_start.X - _dragImage.X, _start.Y - _dragImage.Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!DrawMode) return;
        var local = e.GetPosition(this);
        _hover = local;
        var pos = ToContent(local);
        if (!_drawing)
        {
            if (Tool == SketchTool.Eraser) InvalidateVisual();
            if (Tool == SketchTool.Move)
            {
                var sel = Document.ActiveLayer.Images.FirstOrDefault(i => i.Id == Selected);
                Cursor = new Cursor(sel != null && InkOps.OnHandle(sel, pos.X, pos.Y) ? StandardCursorType.BottomRightCorner
                    : InkOps.HitImage(Document.ActiveLayer, pos.X, pos.Y) >= 0 ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
            }
            return;
        }
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (_gestureTool)
        {
            case SketchTool.Pen:
                foreach (var p in e.GetIntermediatePoints(this)) AddLive(ToContent(p.Position));
                AddLive(pos);
                break;
            case SketchTool.Eraser:
                foreach (var p in e.GetIntermediatePoints(this)) EraseTo(ToContent(p.Position));
                EraseTo(pos);
                break;
            case SketchTool.Ruler:
                _last = pos;
                var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, pos.X, pos.Y, _snap);
                Info?.Invoke(RulerText(SketchOps.Length(_start.X, _start.Y, x, y), SketchOps.RulerAngle(_start.X, _start.Y, x, y)));
                break;
            case SketchTool.Move:
                MoveTo(pos);
                break;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    private void MoveTo(Point pos)
    {
        if (_dragImage == null) return;
        var layer = Document.ActiveLayer;
        var index = layer.Images.FindIndex(i => i.Id == _dragImage.Id);
        if (index < 0) return;
        var next = _resizing
            ? InkOps.Resize(_dragImage, pos.X, pos.Y, free: _snap)
            : _dragImage with { X = Math.Round(pos.X - _grab.X, 1), Y = Math.Round(pos.Y - _grab.Y, 1) };
        if (next == layer.Images[index]) return;
        if (!_changed) { Committing?.Invoke(); _changed = true; }
        layer.Images[index] = next;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drawing) return;
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var pos = ToContent(e.GetPosition(this));
        if (_gestureTool == SketchTool.Pen) AddLive(pos);
        if (_gestureTool == SketchTool.Ruler) _last = pos;
        if (_gestureTool == SketchTool.Move) MoveTo(pos);
        Finish();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drawing) Finish();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!DrawMode || (e.KeyModifiers & KeyModifiers.Control) != 0) return; // Ctrl+wheel is zoom (the window handles it)
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        ScrollBy?.Invoke(shift ? new Vector(-e.Delta.Y * 50, 0) : new Vector(-e.Delta.X * 50, -e.Delta.Y * 50));
        e.Handled = true;
        InvalidateVisual();
    }

    private void AddLive(Point p)
    {
        var n = _live.Count;
        if (n >= 2 && Math.Abs(_live[n - 2] - p.X) + Math.Abs(_live[n - 1] - p.Y) < MinStep) return;
        if (n / 2 >= SketchOps.MaxPointsPerStroke) return;
        _live.Add(p.X);
        _live.Add(p.Y);
    }

    private void EraseTo(Point p)
    {
        var layer = Document.ActiveLayer;
        var left = SketchOps.Erase(layer.Strokes, _last.X, _last.Y, p.X, p.Y, EraserRadius);
        _last = p;
        if (left == null) return;
        if (!_changed) { Committing?.Invoke(); _changed = true; }
        layer.Strokes = left;
        Refresh();
    }

    private void Finish()
    {
        _drawing = false;
        SketchStroke? stroke = null;
        if (_gestureTool == SketchTool.Pen && _live.Count >= 2)
            stroke = new SketchStroke(Document.PenColor, Document.PenWidth, 100, [.. _live]);
        else if (_gestureTool == SketchTool.Ruler)
        {
            var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, _last.X, _last.Y, _snap);
            if (SketchOps.Length(_start.X, _start.Y, x, y) >= 1)
                stroke = new SketchStroke(Document.PenColor, Document.PenWidth, 100, [_start.X, _start.Y, x, y], ruler: true);
            Info?.Invoke("");
        }
        _live.Clear();
        _dragImage = null;
        if (stroke != null)
        {
            if (Document.TotalPoints + stroke.PointCount > SketchOps.MaxPointsTotal) Full?.Invoke();
            else
            {
                Committing?.Invoke();
                Document.ActiveLayer.Strokes.Add(stroke);
                _changed = true;
            }
        }
        if (_changed) Changed?.Invoke();
        _changed = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (DrawMode) context.FillRectangle(Brushes.Transparent, bounds); // takes the mouse everywhere
        var offset = Offset();
        using (context.PushTransform(Matrix.CreateTranslation(-offset.X, -offset.Y)))
        {
            foreach (var layer in Document.Layers)
            {
                if (!layer.Visible) continue;
                using var faded = context.PushOpacity(layer.Opacity / 100.0); // the layer's clarity, 0–100 %
                foreach (var img in layer.Images)
                {
                    var rect = new Rect(img.X, img.Y, img.W, img.H);
                    if (ImageFor(img.File) is { } bmp) context.DrawImage(bmp, rect);
                    else context.FillRectangle(MissingFill, rect);
                }
                foreach (var s in layer.Strokes)
                {
                    if (!_geometry.TryGetValue(s, out var g)) _geometry[s] = g = BuildGeometry(s.Points, s.Ruler);
                    DrawStroke(context, g, s.Color, s.Width, s.Sharpness);
                }
            }

            using var activeFaded = context.PushOpacity(Document.ActiveLayer.Opacity / 100.0); // a line being drawn looks as it will
            if (_drawing && _gestureTool == SketchTool.Pen && _live.Count >= 2)
                DrawStroke(context, BuildGeometry([.. _live], false), Document.PenColor, Document.PenWidth, 100);

            if (_drawing && _gestureTool == SketchTool.Ruler)
            {
                var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, _last.X, _last.Y, _snap);
                DrawRulerGuide(context, new Point(x, y), bounds.Size);
                DrawStroke(context, BuildGeometry([_start.X, _start.Y, x, y], true), Document.PenColor, Document.PenWidth, 100);
                DrawRulerLabel(context, new Point(x, y));
            }

            if (DrawMode && Selected != null && Document.ActiveLayer.Images.FirstOrDefault(i => i.Id == Selected) is { } sel)
            {
                context.DrawRectangle(null, SelectPen, new Rect(sel.X - 1, sel.Y - 1, sel.W + 2, sel.H + 2));
                var h = InkOps.HandleSize;
                context.FillRectangle(HandleFill, new Rect(sel.X + sel.W - h / 2, sel.Y + sel.H - h / 2, h, h), 2);
            }
        }

        if (DrawMode)
        {
            context.DrawRectangle(null, ModeEdge, bounds.Deflate(1));
            if ((Tool == SketchTool.Eraser || (_drawing && _gestureTool == SketchTool.Eraser)) && _hover is { } hv)
                context.DrawEllipse(EraserFill, EraserRing, hv, EraserRadius, EraserRadius);
        }
    }

    private void DrawRulerGuide(DrawingContext context, Point end, Size size)
    {
        var dx = end.X - _start.X;
        var dy = end.Y - _start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len;
        var uy = dy / len;
        var reach = Math.Max(size.Width, size.Height) + Math.Abs(Offset().X) + Math.Abs(Offset().Y);
        context.DrawLine(GuidePen, new Point(_start.X - ux * reach, _start.Y - uy * reach), new Point(end.X + ux * reach, end.Y + uy * reach));
    }

    private static string RulerText(double len, int angle) =>
        Localizer.T("sketch.ruler_label", Math.Round(len).ToString(CultureInfo.InvariantCulture), angle);

    private void DrawRulerLabel(DrawingContext context, Point end)
    {
        var text = new FormattedText(RulerText(SketchOps.Length(_start.X, _start.Y, end.X, end.Y), SketchOps.RulerAngle(_start.X, _start.Y, end.X, end.Y)),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(AppFonts.Thai), 11, LabelFg);
        var at = new Point(end.X + 10, end.Y - text.Height - 8);
        context.FillRectangle(LabelBg, new Rect(at.X - 4, at.Y - 1, text.Width + 8, text.Height + 2), 4);
        context.DrawText(text, at);
    }

    private static void DrawStroke(DrawingContext context, Geometry g, string color, double width, int sharpness)
    {
        var c = Color.TryParse(color, out var parsed) ? parsed : Colors.Black;
        foreach (var (w, opacity) in SketchOps.Layers(width, sharpness))
            context.DrawGeometry(null, new Pen(new SolidColorBrush(c, opacity), w, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
    }

    /// <summary>A pen stroke is smoothed through the midpoints of its points; a ruler line is straight.</summary>
    public static Geometry BuildGeometry(double[] p, bool straight)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        var n = p.Length / 2;
        ctx.BeginFigure(new Point(p[0], p[1]), false);
        if (n == 1) ctx.LineTo(new Point(p[0] + 0.01, p[1])); // a dot: the round cap draws it
        else if (straight || n == 2)
            for (var k = 1; k < n; k++) ctx.LineTo(new Point(p[2 * k], p[2 * k + 1]));
        else
        {
            for (var k = 1; k < n - 1; k++)
                ctx.QuadraticBezierTo(new Point(p[2 * k], p[2 * k + 1]), new Point((p[2 * k] + p[2 * k + 2]) / 2, (p[2 * k + 1] + p[2 * k + 3]) / 2));
            ctx.LineTo(new Point(p[2 * n - 2], p[2 * n - 1]));
        }
        ctx.EndFigure(false);
        return g;
    }
}
