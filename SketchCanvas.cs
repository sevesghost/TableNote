using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TableNote;

/// <summary>
/// 2.6.1.1: the paper of the drawing pad. Draws the current page of a <see cref="SketchDocument"/> (on lined paper if
/// asked) and takes the pen, the eraser and the ruler from the mouse, a pen or a finger. A pen's eraser end erases.
/// </summary>
public sealed class SketchCanvas : Control
{
    public const double LineGap = 32;
    public const double MarginX = 56;
    private const double MinStep = 0.8;

    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#FFFEF8"));
    private static readonly IPen RuleLine = new Pen(new SolidColorBrush(Color.Parse("#D5E3F0")), 1);
    private static readonly IPen MarginLine = new Pen(new SolidColorBrush(Color.Parse("#F2B8B8")), 1);
    private static readonly IPen EraserRing = new Pen(new SolidColorBrush(Color.Parse("#8A7A55")), 1, new DashStyle([3, 2], 0));
    private static readonly IBrush EraserFill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 192, 138, 30)), 1, new DashStyle([4, 3], 0));
    private static readonly IBrush LabelFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush LabelBg = new SolidColorBrush(Color.FromArgb(225, 255, 248, 224));

    private readonly Dictionary<SketchStroke, Geometry> _geometry = new(ReferenceEqualityComparer.Instance);
    private readonly List<double> _live = [];
    private bool _drawing;
    private bool _changedThisGesture;
    private SketchTool _gestureTool;
    private Point _start;
    private Point _last;
    private Point? _hover;
    private bool _snap;

    public SketchCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public SketchDocument Document { get; set; } = new();

    private SketchTool _tool = SketchTool.Pen;
    public SketchTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Cursor = new Cursor(value == SketchTool.Eraser ? StandardCursorType.None : StandardCursorType.Cross);
            InvalidateVisual();
        }
    }

    /// <summary>Called once per gesture, just before the page changes (the window keeps an undo step).</summary>
    public event Action? Committing;
    /// <summary>The page changed (after a stroke, or at the end of an erase).</summary>
    public event Action? Changed;
    /// <summary>A stroke was not kept because the pad is full.</summary>
    public event Action? Full;
    /// <summary>The ruler is being dragged: length in px and angle in degrees, or null when it ends.</summary>
    public event Action<double, int>? RulerMoved;

    public bool IsDrawing => _drawing;

    /// <summary>Redraws after the document or page was replaced from outside (undo, page change, clear).</summary>
    public void Refresh()
    {
        var alive = new HashSet<SketchStroke>(Document.Pages.SelectMany(p => p.Strokes), ReferenceEqualityComparer.Instance);
        foreach (var gone in _geometry.Keys.Where(k => !alive.Contains(k)).ToList()) _geometry.Remove(gone);
        InvalidateVisual();
    }

    private double EraserRadius => Document.EraserWidth / 2;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsEraser) return;
        Focus();
        e.Pointer.Capture(this);
        _drawing = true;
        _changedThisGesture = false;
        _gestureTool = point.Properties.IsEraser ? SketchTool.Eraser : Tool;
        _start = _last = point.Position;
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        _live.Clear();
        if (_gestureTool == SketchTool.Eraser) EraseTo(_start);
        else { _live.Add(_start.X); _live.Add(_start.Y); }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _hover = pos;
        if (!_drawing)
        {
            if (Tool == SketchTool.Eraser) InvalidateVisual();
            return;
        }
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (_gestureTool)
        {
            case SketchTool.Pen:
                foreach (var p in e.GetIntermediatePoints(this)) AddLive(p.Position);
                AddLive(pos);
                break;
            case SketchTool.Eraser:
                foreach (var p in e.GetIntermediatePoints(this)) EraseTo(p.Position);
                EraseTo(pos);
                break;
            case SketchTool.Ruler:
                _last = pos;
                var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, pos.X, pos.Y, _snap);
                RulerMoved?.Invoke(SketchOps.Length(_start.X, _start.Y, x, y), SketchOps.RulerAngle(_start.X, _start.Y, x, y));
                break;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drawing) return;
        _snap = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (_gestureTool == SketchTool.Pen) AddLive(e.GetPosition(this));
        if (_gestureTool == SketchTool.Ruler) _last = e.GetPosition(this);
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
        var page = Document.Current;
        var left = SketchOps.Erase(page.Strokes, _last.X, _last.Y, p.X, p.Y, EraserRadius);
        _last = p;
        if (left == null) return;
        if (!_changedThisGesture) { Committing?.Invoke(); _changedThisGesture = true; }
        page.Strokes = left;
        Refresh();
    }

    private void Finish()
    {
        _drawing = false;
        var tool = _gestureTool;
        SketchStroke? stroke = null;
        if (tool == SketchTool.Pen && _live.Count >= 2)
            stroke = new SketchStroke(Document.PenColor, Document.PenWidth, Document.Sharpness, [.. _live]);
        else if (tool == SketchTool.Ruler)
        {
            var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, _last.X, _last.Y, _snap);
            if (SketchOps.Length(_start.X, _start.Y, x, y) >= 1)
                stroke = new SketchStroke(Document.PenColor, Document.PenWidth, Document.Sharpness, [_start.X, _start.Y, x, y], ruler: true);
            RulerMoved?.Invoke(-1, 0);
        }
        _live.Clear();
        if (stroke != null)
        {
            if (Document.TotalPoints + stroke.PointCount > SketchOps.MaxPointsTotal) Full?.Invoke();
            else
            {
                Committing?.Invoke();
                Document.Current.Strokes.Add(stroke);
                _changedThisGesture = true;
            }
        }
        if (_changedThisGesture) Changed?.Invoke();
        _changedThisGesture = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Paper, bounds);
        if (Document.Lined)
        {
            for (var y = LineGap * 2; y < bounds.Height; y += LineGap)
                context.DrawLine(RuleLine, new Point(0, y + 0.5), new Point(bounds.Width, y + 0.5));
            context.DrawLine(MarginLine, new Point(MarginX + 0.5, 0), new Point(MarginX + 0.5, bounds.Height));
        }

        foreach (var s in Document.Current.Strokes)
        {
            if (!_geometry.TryGetValue(s, out var g)) _geometry[s] = g = BuildGeometry(s.Points, s.Ruler);
            DrawStroke(context, g, s.Color, s.Width, s.Sharpness);
        }

        if (_drawing && _gestureTool == SketchTool.Pen && _live.Count >= 2)
            DrawStroke(context, BuildGeometry([.. _live], false), Document.PenColor, Document.PenWidth, Document.Sharpness);

        if (_drawing && _gestureTool == SketchTool.Ruler)
        {
            var (x, y) = SketchOps.RulerEnd(_start.X, _start.Y, _last.X, _last.Y, _snap);
            var end = new Point(x, y);
            DrawRulerGuide(context, end, bounds);
            DrawStroke(context, BuildGeometry([_start.X, _start.Y, x, y], true), Document.PenColor, Document.PenWidth, Document.Sharpness);
            DrawRulerLabel(context, end);
        }

        if ((Tool == SketchTool.Eraser || (_drawing && _gestureTool == SketchTool.Eraser)) && _hover is { } h)
        {
            var r = EraserRadius;
            context.DrawEllipse(EraserFill, EraserRing, h, r, r);
        }
    }

    private void DrawRulerGuide(DrawingContext context, Point end, Rect bounds)
    {
        // The ruler's edge, a little past both ends, so it is clear where the line will go.
        var dx = end.X - _start.X;
        var dy = end.Y - _start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len;
        var uy = dy / len;
        var reach = Math.Max(bounds.Width, bounds.Height);
        context.DrawLine(GuidePen, new Point(_start.X - ux * reach, _start.Y - uy * reach), new Point(end.X + ux * reach, end.Y + uy * reach));
    }

    private void DrawRulerLabel(DrawingContext context, Point end)
    {
        var len = SketchOps.Length(_start.X, _start.Y, end.X, end.Y);
        var angle = SketchOps.RulerAngle(_start.X, _start.Y, end.X, end.Y);
        var text = new FormattedText(Localizer.T("sketch.ruler_label", Math.Round(len).ToString(CultureInfo.InvariantCulture), angle),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(AppFonts.Thai), 11, LabelFg);
        var at = new Point(Math.Min(end.X + 10, Bounds.Width - text.Width - 6), Math.Max(4, end.Y - text.Height - 8));
        context.FillRectangle(LabelBg, new Rect(at.X - 4, at.Y - 1, text.Width + 8, text.Height + 2), 4);
        context.DrawText(text, at);
    }

    private static void DrawStroke(DrawingContext context, Geometry g, string color, double width, int sharpness)
    {
        var c = Color.TryParse(color, out var parsed) ? parsed : Colors.Black;
        foreach (var (w, opacity) in SketchOps.Layers(width, sharpness))
        {
            var brush = new SolidColorBrush(c, opacity);
            context.DrawGeometry(null, new Pen(brush, w, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
        }
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
            {
                var c = new Point(p[2 * k], p[2 * k + 1]);
                var mid = new Point((p[2 * k] + p[2 * k + 2]) / 2, (p[2 * k + 1] + p[2 * k + 3]) / 2);
                ctx.QuadraticBezierTo(c, mid);
            }
            ctx.LineTo(new Point(p[2 * n - 2], p[2 * n - 1]));
        }
        ctx.EndFigure(false);
        return g;
    }
}
