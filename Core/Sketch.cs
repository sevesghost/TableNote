using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TableNote;

/// <summary>
/// 2.6.1.1: drawing on the table (the Draw button). One stroke of the pen or the ruler: its colour, width, sharpness
/// (0 = soft edge, 100 = crisp) and its points as x0,y0,x1,y1,… in table coordinates (scrolling moves them with the table).
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

public enum SketchTool { Pen, Eraser, Ruler, Move }

/// <summary>Pure drawing-pad operations (no UI), so they can be tested.</summary>
public static class SketchOps
{
    public const string DefaultColor = "#1F2937";
    public const double DefaultPenWidth = 3;
    public const double DefaultEraserWidth = 20;
    public const double MinWidth = 1;
    public const double MaxWidth = 60;
    public const int MaxPointsPerStroke = 20_000;
    /// <summary>All layers together; keeps layers.json well under <see cref="InkStore.MaxFileBytes"/>.</summary>
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

/// <summary>A picture placed on a layer, in table coordinates. <see cref="File"/> is a name in the images folder.</summary>
public sealed record SketchImage(string Id, string File, double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
}

/// <summary>One layer over the table: its drawing and pictures. A hidden layer is kept but not drawn.</summary>
public sealed class InkLayer
{
    public string Id { get; set; } = InkOps.NewId();
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public List<SketchStroke> Strokes { get; set; } = [];
    public List<SketchImage> Images { get; set; } = [];

    /// <summary>A copy for undo (strokes and pictures never change, so the lists can be shallow copies).</summary>
    public InkLayer Clone() => new() { Id = Id, Name = Name, Visible = Visible, Strokes = [.. Strokes], Images = [.. Images] };
}

/// <summary>A picture in a cell: the file in the images folder and whether the cell shows it open.</summary>
public sealed record CellImage(string File, bool Open);

/// <summary>Everything in layers.json: the layers over the table, the tool settings, and the pictures in cells.</summary>
public sealed class InkDocument
{
    public List<InkLayer> Layers { get; set; } = [];
    public int Active { get; set; }
    public string PenColor { get; set; } = SketchOps.DefaultColor;
    public double PenWidth { get; set; } = SketchOps.DefaultPenWidth;
    public int Sharpness { get; set; } = 100;
    public double EraserWidth { get; set; } = SketchOps.DefaultEraserWidth;
    /// <summary>Pictures in cells, by <see cref="CellDetailOps.Key"/> (row id + column id).</summary>
    public Dictionary<string, CellImage> CellImages { get; set; } = [];

    /// <summary>The layer that is drawn on; made when there is none.</summary>
    public InkLayer ActiveLayer
    {
        get
        {
            if (Layers.Count == 0) Layers.Add(new InkLayer { Name = InkOps.DefaultLayerName(1) });
            Active = Math.Clamp(Active, 0, Layers.Count - 1);
            return Layers[Active];
        }
    }

    public int TotalPoints => Layers.Sum(l => l.Strokes.Sum(s => s.PointCount));
    public int TotalImages => Layers.Sum(l => l.Images.Count);
    public bool HasInk => Layers.Any(l => l.Strokes.Count > 0 || l.Images.Count > 0);
}

/// <summary>Layer, picture and cell-picture operations (no UI).</summary>
public static class InkOps
{
    public const int MaxLayers = 10;
    public const int MaxImages = 100;
    public const int MaxLayerName = 40;
    public const double MinImageSize = 16;
    /// <summary>A new picture on a layer is at most this wide or tall at first.</summary>
    public const double PlaceSize = 320;
    public const double HandleSize = 12;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static string DefaultLayerName(int n) => Localizer.T("ink.layer_name", n);

    /// <summary>Adds a layer above the active one and makes it active; null when there are already <see cref="MaxLayers"/>.</summary>
    public static InkLayer? AddLayer(InkDocument doc, string? name = null)
    {
        if (doc.Layers.Count >= MaxLayers) return null;
        var n = 1;
        while (doc.Layers.Any(l => l.Name == DefaultLayerName(n))) n++;
        var layer = new InkLayer { Name = CleanName(name) is { Length: > 0 } c ? c : DefaultLayerName(n) };
        var at = doc.Layers.Count == 0 ? 0 : Math.Clamp(doc.Active + 1, 0, doc.Layers.Count);
        doc.Layers.Insert(at, layer);
        doc.Active = at;
        return layer;
    }

    /// <summary>Removes the active layer; the one below becomes active. The last layer is emptied instead.</summary>
    public static void DeleteActiveLayer(InkDocument doc)
    {
        _ = doc.ActiveLayer;
        if (doc.Layers.Count == 1)
        {
            var only = doc.Layers[0];
            doc.Layers[0] = new InkLayer { Id = only.Id, Name = only.Name, Visible = only.Visible };
            return;
        }
        doc.Layers.RemoveAt(doc.Active);
        doc.Active = Math.Clamp(doc.Active - 1, 0, doc.Layers.Count - 1);
    }

    public static string CleanName(string? name)
    {
        var s = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length > MaxLayerName ? s[..MaxLayerName] : s;
    }

    /// <summary>Size of a new picture: its own size, made smaller (same shape) to fit <see cref="PlaceSize"/>.</summary>
    public static (double W, double H) PlaceSizeFor(double pixelWidth, double pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return (PlaceSize, PlaceSize);
        var scale = Math.Min(1, PlaceSize / Math.Max(pixelWidth, pixelHeight));
        return (Math.Max(MinImageSize, pixelWidth * scale), Math.Max(MinImageSize, pixelHeight * scale));
    }

    /// <summary>The topmost picture of the layer under the point, or -1.</summary>
    public static int HitImage(InkLayer layer, double x, double y)
    {
        for (var i = layer.Images.Count - 1; i >= 0; i--)
            if (layer.Images[i].Contains(x, y)) return i;
        return -1;
    }

    /// <summary>True when the point is on the resize handle (bottom-right corner) of the picture.</summary>
    public static bool OnHandle(SketchImage img, double x, double y) =>
        Math.Abs(x - (img.X + img.W)) <= HandleSize && Math.Abs(y - (img.Y + img.H)) <= HandleSize;

    /// <summary>The picture dragged by its bottom-right corner to (x, y): same shape unless <paramref name="free"/>.</summary>
    public static SketchImage Resize(SketchImage img, double x, double y, bool free)
    {
        var w = Math.Max(MinImageSize, x - img.X);
        var h = Math.Max(MinImageSize, y - img.Y);
        if (!free && img.W > 0 && img.H > 0)
        {
            // Same shape, following the direction dragged further.
            var ratio = img.W / img.H;
            if (w / img.W >= h / img.H) h = w / ratio; else w = h * ratio;
            if (w < MinImageSize) { w = MinImageSize; h = w / ratio; }
            if (h < MinImageSize) { h = MinImageSize; w = h * ratio; }
        }
        return img with { W = Math.Round(w, 1), H = Math.Round(h, 1) };
    }

    /// <summary>Every picture file that is still used (on a layer or in a cell).</summary>
    public static HashSet<string> UsedFiles(InkDocument doc) =>
        new(doc.Layers.SelectMany(l => l.Images.Select(i => i.File)).Concat(doc.CellImages.Values.Select(c => c.File)), StringComparer.OrdinalIgnoreCase);

    public static InkDocument CloneLayers(InkDocument doc) => new()
    {
        Layers = doc.Layers.Select(l => l.Clone()).ToList(),
        Active = doc.Active
    };
}

/// <summary>
/// layers.json next to note.json, pictures in the images folder beside it. note.json itself is not changed.
/// Written to a .tmp file first and swapped in, keeping the one before as layers.json.prev. A file that can't be read
/// is kept aside as layers.json.bad-yyyyMMdd-HHmmss and the layers start empty.
/// </summary>
public static class InkStore
{
    public const int FileVersion = 1;
    public const int MaxFileBytes = 16 * 1024 * 1024;
    public const long MaxImageBytes = 20L * 1024 * 1024;
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public static string PathFor(string notePath) => Path.Combine(Path.GetDirectoryName(notePath) ?? ".", "layers.json");
    public static string ImagesDir(string notePath) => Path.Combine(Path.GetDirectoryName(notePath) ?? ".", "images");
    public static string ImagePath(string notePath, string file) => Path.Combine(ImagesDir(notePath), Path.GetFileName(file));

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Copies a picture into the images folder under a new name and returns that name.</summary>
    public static string Import(string notePath, string source)
    {
        if (!IsImageFile(source)) throw new InvalidDataException(Localizer.T("ink.image_type"));
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException(source);
        if (info.Length > MaxImageBytes) throw new InvalidDataException(Localizer.T("ink.image_too_big", MaxImageBytes / 1024 / 1024));
        var dir = ImagesDir(notePath);
        Directory.CreateDirectory(dir);
        var name = "img-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + InkOps.NewId()[..6] +
                   Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(dir, name));
        return name;
    }

    internal sealed class InkFileDto
    {
        public int Version { get; set; } = FileVersion;
        public int Active { get; set; }
        public string? PenColor { get; set; }
        public double PenWidth { get; set; } = SketchOps.DefaultPenWidth;
        public int Sharpness { get; set; } = 100;
        public double EraserWidth { get; set; } = SketchOps.DefaultEraserWidth;
        public List<LayerDto>? Layers { get; set; }
        public Dictionary<string, CellImageDto>? CellImages { get; set; }
    }

    internal sealed class LayerDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool Visible { get; set; } = true;
        public List<StrokeDto>? Strokes { get; set; }
        public List<ImageDto>? Images { get; set; }
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

    internal sealed class ImageDto
    {
        public string? Id { get; set; }
        public string? File { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }

    internal sealed class CellImageDto
    {
        public string? File { get; set; }
        public bool Open { get; set; }
    }

    private static readonly TableNoteJson Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });

    /// <summary>Reads layers.json; a missing file is no layers. <paramref name="warning"/> is set when a broken file was put aside.</summary>
    public static InkDocument Load(string path, out string? warning)
    {
        warning = null;
        if (!File.Exists(path)) return new InkDocument();
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("layers.json is larger than " + MaxFileBytes + " bytes");
            var dto = JsonSerializer.Deserialize(File.ReadAllText(path), Json.InkFileDto) ?? throw new InvalidDataException("empty");
            return FromDto(dto);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            var bad = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try { File.Move(path, bad, overwrite: true); } catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException) { bad = path; }
            AppLog.Write("ink", "could not read layers.json (" + ex.Message + "); kept as " + Path.GetFileName(bad));
            warning = Path.GetFileName(bad);
            return new InkDocument();
        }
    }

    public static void Save(string path, InkDocument doc)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var text = JsonSerializer.Serialize(ToDto(doc), Json.InkFileDto);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, path + ".prev", ignoreMetadataErrors: true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                AppLog.Write("ink", "File.Replace failed (" + ex.Message + ") → move");
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    internal static InkFileDto ToDto(InkDocument doc) => new()
    {
        Version = FileVersion,
        Active = doc.Active,
        PenColor = doc.PenColor,
        PenWidth = doc.PenWidth,
        Sharpness = doc.Sharpness,
        EraserWidth = doc.EraserWidth,
        Layers = doc.Layers.Select(l => new LayerDto
        {
            Id = l.Id,
            Name = l.Name,
            Visible = l.Visible,
            Strokes = l.Strokes.Select(s => new StrokeDto
            {
                Color = s.Color, Width = s.Width, Sharpness = s.Sharpness, Ruler = s.Ruler,
                Points = s.Points.Select(v => Math.Round(v, 1)).ToArray()
            }).ToList(),
            Images = l.Images.Select(i => new ImageDto { Id = i.Id, File = i.File, X = Math.Round(i.X, 1), Y = Math.Round(i.Y, 1), W = i.W, H = i.H }).ToList()
        }).ToList(),
        CellImages = doc.CellImages.ToDictionary(kv => kv.Key, kv => new CellImageDto { File = kv.Value.File, Open = kv.Value.Open })
    };

    private static bool GoodFile(string? f) => !string.IsNullOrWhiteSpace(f) && Path.GetFileName(f) == f && IsImageFile(f);

    internal static InkDocument FromDto(InkFileDto dto)
    {
        var layers = (dto.Layers ?? []).Take(InkOps.MaxLayers).Select((l, n) => new InkLayer
        {
            Id = string.IsNullOrWhiteSpace(l.Id) ? InkOps.NewId() : l.Id!,
            Name = InkOps.CleanName(l.Name) is { Length: > 0 } name ? name : InkOps.DefaultLayerName(n + 1),
            Visible = l.Visible,
            Strokes = (l.Strokes ?? [])
                .Where(s => s.Points is { Length: >= 2 } && s.Points.All(double.IsFinite))
                .Select(s => new SketchStroke(s.Color ?? SketchOps.DefaultColor, s.Width, s.Sharpness,
                    s.Points!.Length > 2 * SketchOps.MaxPointsPerStroke ? s.Points[..(2 * SketchOps.MaxPointsPerStroke)] : s.Points, s.Ruler))
                .ToList(),
            Images = (l.Images ?? [])
                .Where(i => GoodFile(i.File) && double.IsFinite(i.X) && double.IsFinite(i.Y) && double.IsFinite(i.W) && double.IsFinite(i.H))
                .Select(i => new SketchImage(string.IsNullOrWhiteSpace(i.Id) ? InkOps.NewId() : i.Id!, i.File!, i.X, i.Y,
                    Math.Max(InkOps.MinImageSize, i.W), Math.Max(InkOps.MinImageSize, i.H)))
                .ToList()
        }).ToList();
        return new InkDocument
        {
            Layers = layers,
            Active = layers.Count == 0 ? 0 : Math.Clamp(dto.Active, 0, layers.Count - 1),
            PenColor = SketchOps.NormalizeColor(dto.PenColor),
            PenWidth = SketchOps.ClampWidth(dto.PenWidth),
            Sharpness = SketchOps.ClampSharpness(dto.Sharpness),
            EraserWidth = SketchOps.ClampWidth(dto.EraserWidth),
            CellImages = (dto.CellImages ?? [])
                .Where(kv => CellDetailOps.TrySplit(kv.Key, out _, out _) && GoodFile(kv.Value?.File))
                .ToDictionary(kv => kv.Key, kv => new CellImage(kv.Value.File!, kv.Value.Open))
        };
    }
}
