using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TableNote;

/// <summary>
/// 2.6.1.1: drawing pad (Table → Drawing pad…, or the Draw button). One stroke of the pen or the ruler: its colour,
/// width, sharpness (0 = soft edge, 100 = crisp) and its points as x0,y0,x1,y1,… in the pad's coordinates.
/// A stroke never changes once made; the eraser replaces it with the pieces that are left.
/// </summary>
public sealed class SketchStroke
{
    public SketchStroke(string color, double width, int sharpness, double[] points, bool ruler = false)
    {
        Color = SketchOps.NormalizeColor(color);
        Width = SketchOps.ClampWidth(width);
        Sharpness = SketchOps.ClampSharpness(sharpness);
        Points = points.Length % 2 == 0 ? points : points[..^1];
        Ruler = ruler;
    }

    public string Color { get; }
    public double Width { get; }
    public int Sharpness { get; }
    public double[] Points { get; }
    public bool Ruler { get; }
    public int PointCount => Points.Length / 2;

    public SketchStroke WithPoints(double[] points) => new(Color, Width, Sharpness, points, Ruler);
}

/// <summary>One page of the drawing pad.</summary>
public sealed class SketchPage
{
    public List<SketchStroke> Strokes { get; set; } = [];
}

/// <summary>Everything in sketch.json: the pages, the page shown, and the tool settings last used.</summary>
public sealed class SketchDocument
{
    public List<SketchPage> Pages { get; set; } = [new SketchPage()];
    public int Page { get; set; }
    public bool Lined { get; set; } = true;
    public string PenColor { get; set; } = SketchOps.DefaultColor;
    public double PenWidth { get; set; } = SketchOps.DefaultPenWidth;
    public int Sharpness { get; set; } = 100;
    public double EraserWidth { get; set; } = SketchOps.DefaultEraserWidth;

    public SketchPage Current
    {
        get
        {
            if (Pages.Count == 0) Pages.Add(new SketchPage());
            Page = Math.Clamp(Page, 0, Pages.Count - 1);
            return Pages[Page];
        }
    }

    public int TotalPoints => Pages.Sum(p => p.Strokes.Sum(s => s.PointCount));
}

public enum SketchTool { Pen, Eraser, Ruler }

/// <summary>Pure drawing-pad operations (no UI), so they can be tested.</summary>
public static class SketchOps
{
    public const string DefaultColor = "#1F2937";
    public const double DefaultPenWidth = 3;
    public const double DefaultEraserWidth = 20;
    public const double MinWidth = 1;
    public const double MaxWidth = 60;
    public const int MaxPages = 50;
    public const int MaxPointsPerStroke = 20_000;
    /// <summary>All pages together; keeps sketch.json well under <see cref="SketchStore.MaxFileBytes"/>.</summary>
    public const int MaxPointsTotal = 400_000;
    /// <summary>Angle step of the ruler while Shift is held.</summary>
    public const double SnapDegrees = 15;

    /// <summary>Pen colours offered as swatches (the hex box takes any other).</summary>
    public static readonly string[] Palette =
    [
        "#1F2937", "#6B7280", "#DC2626", "#EA580C", "#CA8A04",
        "#16A34A", "#0891B2", "#2563EB", "#7C3AED", "#DB2777",
        "#FFFFFF"
    ];

    public static int ClampSharpness(int value) => Math.Clamp(value, 0, 100);
    public static double ClampWidth(double value) => double.IsFinite(value) ? Math.Clamp(value, MinWidth, MaxWidth) : DefaultPenWidth;

    /// <summary>"#rgb", "#rrggbb", "rrggbb" (any case) → "#RRGGBB"; anything else is null.</summary>
    public static string? ParseColor(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        return "#" + s.ToUpperInvariant();
    }

    public static string NormalizeColor(string? text) => ParseColor(text) ?? DefaultColor;

    /// <summary>
    /// Sharpness 0–100 as layers to draw, widest first: each is (width, opacity). 100 is one crisp line; lower values
    /// put a softer, wider edge around a thinner core, so the line looks blurred but stays readable at 0.
    /// </summary>
    public static IReadOnlyList<(double Width, double Opacity)> Layers(double width, int sharpness)
    {
        var s = ClampSharpness(sharpness);
        if (s >= 100) return [(width, 1)];
        var soft = 1 - s / 100.0;
        var core = Math.Max(0.5, width * (1 - 0.55 * soft));
        var halo = width * 2.2 * soft;
        const int rings = 8; // enough that the edge fades without visible bands
        var list = new List<(double, double)>(rings + 1);
        for (var i = rings; i >= 1; i--)
            list.Add((core + halo * i / rings, 0.06 + 0.08 * (1 - soft)));
        list.Add((core, 0.6 + 0.4 * (1 - soft)));
        return list;
    }

    /// <summary>The ruler: end point as dragged, or with Shift on the nearest 15° step at the same length.</summary>
    public static (double X, double Y) RulerEnd(double x0, double y0, double x1, double y1, bool snap)
    {
        if (!snap) return (x1, y1);
        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return (x1, y1);
        var step = SnapDegrees * Math.PI / 180;
        var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        var x = x0 + len * Math.Cos(angle);
        var y = y0 + len * Math.Sin(angle);
        return (Math.Round(x, 3), Math.Round(y, 3));
    }

    /// <summary>Angle of a ruler line in degrees, 0–359, counted like a protractor (up is 90°).</summary>
    public static int RulerAngle(double x0, double y0, double x1, double y1)
    {
        var deg = Math.Atan2(-(y1 - y0), x1 - x0) * 180 / Math.PI;
        var r = (int)Math.Round(deg);
        return ((r % 360) + 360) % 360;
    }

    public static double Length(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    /// <summary>
    /// Erases along the eraser's path from (ax, ay) to (bx, by) with the given radius. A stroke the eraser touches is
    /// split into the pieces that are left (a piece of one point stays as a dot); untouched strokes are kept as they are
    /// (the same objects). Returns null when nothing was touched.
    /// </summary>
    public static List<SketchStroke>? Erase(IReadOnlyList<SketchStroke> strokes, double ax, double ay, double bx, double by, double radius)
    {
        List<SketchStroke>? result = null;
        for (var i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            var reach = radius + s.Width / 2;
            if (!NearBounds(s, ax, ay, bx, by, reach))
            {
                result?.Add(s);
                continue;
            }
            var step = Math.Clamp(Math.Min(reach / 2, 4), 0.5, 4);
            var pts = Densify(s.Points, step);
            var n = pts.Length / 2;
            var hit = new bool[n];
            var any = false;
            for (var k = 0; k < n; k++)
            {
                if (SegmentDistance(pts[2 * k], pts[2 * k + 1], ax, ay, bx, by) <= reach) { hit[k] = true; any = true; }
            }
            if (!any)
            {
                result?.Add(s);
                continue;
            }
            result ??= new List<SketchStroke>(strokes.Take(i));
            var start = -1;
            for (var k = 0; k <= n; k++)
            {
                var keep = k < n && !hit[k];
                if (keep && start < 0) start = k;
                if (!keep && start >= 0)
                {
                    result.Add(s.WithPoints(pts[(2 * start)..(2 * k)]));
                    start = -1;
                }
            }
        }
        return result;
    }

    /// <summary>Adds points so that no two in a row are further apart than <paramref name="step"/>.</summary>
    public static double[] Densify(double[] points, double step)
    {
        var n = points.Length / 2;
        if (n < 2) return points;
        var list = new List<double>(points.Length * 2) { points[0], points[1] };
        for (var k = 1; k < n; k++)
        {
            double x0 = points[2 * k - 2], y0 = points[2 * k - 1], x1 = points[2 * k], y1 = points[2 * k + 1];
            var parts = (int)Math.Ceiling(Length(x0, y0, x1, y1) / step);
            for (var j = 1; j < parts; j++)
            {
                var t = (double)j / parts;
                list.Add(x0 + (x1 - x0) * t);
                list.Add(y0 + (y1 - y0) * t);
            }
            list.Add(x1);
            list.Add(y1);
        }
        return [.. list];
    }

    /// <summary>Distance from point (px, py) to the segment (ax, ay)–(bx, by).</summary>
    public static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 < 1e-12 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        var cx = ax + t * dx - px;
        var cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    private static bool NearBounds(SketchStroke s, double ax, double ay, double bx, double by, double reach)
    {
        if (s.Points.Length < 2) return false;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var k = 0; k + 1 < s.Points.Length; k += 2)
        {
            minX = Math.Min(minX, s.Points[k]); maxX = Math.Max(maxX, s.Points[k]);
            minY = Math.Min(minY, s.Points[k + 1]); maxY = Math.Max(maxY, s.Points[k + 1]);
        }
        return Math.Max(ax, bx) >= minX - reach && Math.Min(ax, bx) <= maxX + reach &&
               Math.Max(ay, by) >= minY - reach && Math.Min(ay, by) <= maxY + reach;
    }
}

/// <summary>
/// sketch.json next to note.json. Written to a .tmp file first and swapped in, keeping the one before as
/// sketch.json.prev. A file that can't be read is kept aside as sketch.json.bad-yyyyMMdd-HHmmss and the pad starts empty.
/// </summary>
public static class SketchStore
{
    public const int FileVersion = 1;
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static string PathFor(string notePath) => Path.Combine(Path.GetDirectoryName(notePath) ?? ".", "sketch.json");

    internal sealed class SketchFileDto
    {
        public int Version { get; set; } = FileVersion;
        public int Page { get; set; }
        public bool Lined { get; set; } = true;
        public string? PenColor { get; set; }
        public double PenWidth { get; set; } = SketchOps.DefaultPenWidth;
        public int Sharpness { get; set; } = 100;
        public double EraserWidth { get; set; } = SketchOps.DefaultEraserWidth;
        public List<PageDto>? Pages { get; set; }
    }

    internal sealed class PageDto
    {
        public List<StrokeDto>? Strokes { get; set; }
    }

    internal sealed class StrokeDto
    {
        public string? Color { get; set; }
        public double Width { get; set; }
        public int Sharpness { get; set; } = 100;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Ruler { get; set; }
        public double[]? Points { get; set; }
    }

    private static readonly TableNoteJson Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });

    /// <summary>Reads sketch.json; a missing file is an empty pad. <paramref name="warning"/> is set when a broken file was put aside.</summary>
    public static SketchDocument Load(string path, out string? warning)
    {
        warning = null;
        if (!File.Exists(path)) return new SketchDocument();
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("sketch.json is larger than " + MaxFileBytes + " bytes");
            var dto = JsonSerializer.Deserialize(File.ReadAllText(path), Json.SketchFileDto) ?? throw new InvalidDataException("empty");
            return FromDto(dto);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            var bad = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try { File.Move(path, bad, overwrite: true); } catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException) { bad = path; }
            AppLog.Write("sketch", "could not read sketch.json (" + ex.Message + "); kept as " + Path.GetFileName(bad));
            warning = Path.GetFileName(bad);
            return new SketchDocument();
        }
    }

    public static void Save(string path, SketchDocument doc)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var text = JsonSerializer.Serialize(ToDto(doc), Json.SketchFileDto);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, path + ".prev", ignoreMetadataErrors: true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                AppLog.Write("sketch", "File.Replace failed (" + ex.Message + ") → move");
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    internal static SketchFileDto ToDto(SketchDocument doc) => new()
    {
        Version = FileVersion,
        Page = doc.Page,
        Lined = doc.Lined,
        PenColor = doc.PenColor,
        PenWidth = doc.PenWidth,
        Sharpness = doc.Sharpness,
        EraserWidth = doc.EraserWidth,
        Pages = doc.Pages.Select(p => new PageDto
        {
            Strokes = p.Strokes.Select(s => new StrokeDto
            {
                Color = s.Color,
                Width = s.Width,
                Sharpness = s.Sharpness,
                Ruler = s.Ruler,
                Points = s.Points.Select(v => Math.Round(v, 1)).ToArray()
            }).ToList()
        }).ToList()
    };

    internal static SketchDocument FromDto(SketchFileDto dto)
    {
        var pages = (dto.Pages ?? []).Take(SketchOps.MaxPages).Select(p => new SketchPage
        {
            Strokes = (p.Strokes ?? [])
                .Where(s => s.Points is { Length: >= 2 } && s.Points.All(double.IsFinite))
                .Select(s => new SketchStroke(s.Color ?? SketchOps.DefaultColor, s.Width, s.Sharpness,
                    s.Points!.Length > 2 * SketchOps.MaxPointsPerStroke ? s.Points[..(2 * SketchOps.MaxPointsPerStroke)] : s.Points, s.Ruler))
                .ToList()
        }).ToList();
        if (pages.Count == 0) pages.Add(new SketchPage());
        return new SketchDocument
        {
            Pages = pages,
            Page = Math.Clamp(dto.Page, 0, pages.Count - 1),
            Lined = dto.Lined,
            PenColor = SketchOps.NormalizeColor(dto.PenColor),
            PenWidth = SketchOps.ClampWidth(dto.PenWidth),
            Sharpness = SketchOps.ClampSharpness(dto.Sharpness),
            EraserWidth = SketchOps.ClampWidth(dto.EraserWidth)
        };
    }
}
