using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TableNote;

/// <summary>
/// Draws the wallpaper picture: the user's wallpaper placed the way Windows places it, with a card on top that
/// holds the alert boxes and the table (the same table as image export). The card sits where the editor window
/// was, at the window's size at most, scaled like the window (monitor scale × TableNote zoom).
/// </summary>
public static class WallpaperImage
{
    public const double CardPadding = 12;
    public const double CardCorner = 10;
    private const double Gap = 8;
    private const double MoreLineHeight = 28;
    /// <summary>Smallest size a wide table is shrunk to on the wallpaper (of the window's zoom).</summary>
    public const double MinTableScale = 0.6;
    private const int MaxDecodeWidth = 8192;

    /// <summary>
    /// The card, in DIPs: alert boxes, then as many table rows as fit in <paramref name="max"/>, then "+ N more rows".
    /// Rows follow hide-done; colours, collapsed columns and row heights come from <paramref name="options"/>.
    /// </summary>
    public static Control BuildCard(NoteDocument doc, ViewImageOptions options, Size max, IBrush background, Func<string, PartColor?> colors)
    {
        var innerMaxWidth = Math.Max(120, max.Width - CardPadding * 2);
        var innerMaxHeight = Math.Max(60, max.Height - CardPadding * 2);
        var status = doc.StatusColumnIndex();
        var visible = Enumerable.Range(0, doc.Rows.Count)
            .Where(r => !(doc.HideDone && status >= 0 && TaskStatuses.IsDone(doc.GetCell(r, status))))
            .ToList();

        var full = ViewImage.Plan(doc, WithRows(options, visible));
        var boxes = (doc.AlertBoxes ?? []).Where(b => b != null && (!string.IsNullOrWhiteSpace(b.Title) || !string.IsNullOrWhiteSpace(b.Text))).ToList();
        // A table wider than the window is drawn smaller (down to MinTableScale), so the columns on the right
        // (status, due date) stay on the wallpaper; anything still wider is cut at the right edge.
        var tableScale = full.Width > innerMaxWidth ? Math.Max(MinTableScale, innerMaxWidth / full.Width) : 1.0;
        var innerWidth = Math.Min(innerMaxWidth, Math.Max(full.Width * tableScale, boxes.Count > 0 ? 320 : 0));

        var stack = new StackPanel { Spacing = Gap };
        var tableRoom = innerMaxHeight;
        if (boxes.Count > 0)
        {
            var alerts = new StackPanel { Spacing = 6 };
            foreach (var box in boxes) alerts.Children.Add(AlertCard(box, colors));
            var alertHost = new Border { Child = alerts, ClipToBounds = true, MaxHeight = Math.Max(60, innerMaxHeight * 0.45) };
            alertHost.Measure(new Size(innerWidth, double.PositiveInfinity));
            stack.Children.Add(alertHost);
            tableRoom -= alertHost.DesiredSize.Height + Gap;
        }

        var plan = full;
        var hidden = 0;
        if (visible.Count > 1 && full.Height * tableScale > tableRoom)
        {
            var room = (tableRoom - MoreLineHeight) / tableScale - full.HeaderHeight;
            var take = 0;
            double used = 0;
            foreach (var row in full.Rows)
            {
                if (take > 0 && used + row.Height > room) break;
                used += row.Height;
                take++;
            }
            take = Math.Clamp(take, 1, visible.Count);
            hidden = visible.Count - take;
            if (hidden > 0) plan = ViewImage.Plan(doc, WithRows(options, visible.Take(take).ToList()));
        }

        var grid = ViewImage.Build(plan);
        grid.HorizontalAlignment = HorizontalAlignment.Left; // cut on the right, never centred
        grid.VerticalAlignment = VerticalAlignment.Top;
        stack.Children.Add(new Border
        {
            Child = new LayoutTransformControl
            {
                LayoutTransform = Math.Abs(tableScale - 1) < 0.001 ? null : new ScaleTransform(tableScale, tableScale),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = grid
            },
            ClipToBounds = true,
            Width = Math.Min(plan.Width * tableScale, innerWidth),
            Height = plan.Height * tableScale,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        if (hidden > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Localizer.T("wallpaper.more_rows", hidden),
                FontFamily = AppFonts.Thai,
                FontSize = 13,
                Foreground = Brush(ViewImage.Text),
                Margin = new Thickness(2, 0, 0, 0)
            });
        }

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(CardCorner),
            BoxShadow = BoxShadows.Parse("0 4 18 0 #59000000"),
            Width = innerWidth + CardPadding * 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Border
            {
                Padding = new Thickness(CardPadding),
                MaxHeight = Math.Max(1, max.Height),
                ClipToBounds = true,
                Child = stack
            }
        };
    }

    /// <summary>The whole wallpaper as PNG: background colour, the user's picture as Windows fits it, the card.</summary>
    /// <param name="at">Card position inside <see cref="WallpaperScene.Area"/>, in pixels.</param>
    /// <param name="scale">Pixels per card DIP (monitor scale × zoom).</param>
    public static byte[] Compose(WallpaperScene scene, Control? card, PixelPoint at, double scale)
    {
        var width = Math.Max(1, scene.Area.Width);
        var height = Math.Max(1, scene.Area.Height);
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(WallpaperGeometry.FromColorRef(scene.BackgroundColor))
        };
        using var picture = LoadPicture(scene.Picture, scene.Fit, width, height);
        if (picture != null)
        {
            if (scene.Fit == WallpaperFit.Tile)
            {
                canvas.Children.Add(new Border
                {
                    Width = width,
                    Height = height,
                    Background = new ImageBrush(picture)
                    {
                        TileMode = TileMode.Tile,
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        DestinationRect = new RelativeRect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height, RelativeUnit.Absolute)
                    }
                });
            }
            else
            {
                var place = WallpaperGeometry.Place(scene.Fit, picture.PixelSize.Width, picture.PixelSize.Height, width, height);
                var image = new Image { Source = picture, Stretch = Stretch.Fill, Width = place.Width, Height = place.Height };
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                Canvas.SetLeft(image, place.X);
                Canvas.SetTop(image, place.Y);
                canvas.Children.Add(image);
            }
        }
        if (card != null)
        {
            var host = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = card };
            Canvas.SetLeft(host, at.X);
            Canvas.SetTop(host, at.Y);
            canvas.Children.Add(host);
        }
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(canvas);
        return ViewImage.EncodeBitmap(bitmap, ViewImageFormat.Png);
    }

    /// <summary>The user's picture, decoded no bigger than it is drawn. Null when there is none or it can't be read
    /// (HEIC, JPEG XR…): then only the colour is drawn under the card, and the file is still what is put back.</summary>
    public static Bitmap? LoadPicture(string? path, WallpaperFit fit, int areaWidth, int areaHeight)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var size = ReadSize(stream);
            stream.Position = 0;
            if (size is { Width: > 0, Height: > 0 } s)
            {
                var want = Math.Min(MaxDecodeWidth, WallpaperGeometry.DecodeWidth(fit, s.Width, s.Height, areaWidth, areaHeight));
                if (want < s.Width) return Bitmap.DecodeToWidth(stream, want, BitmapInterpolationMode.HighQuality);
            }
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            AppLog.Write("wallpaper", "picture not readable (" + ex.GetType().Name + "); drawing the colour only");
            return null;
        }
    }

    /// <summary>Pixel size from the file header (PNG, JPEG, BMP, GIF, WebP) without decoding the picture.</summary>
    public static (int Width, int Height)? ReadSize(Stream stream)
    {
        try
        {
            Span<byte> h = stackalloc byte[32];
            var n = stream.ReadAtLeast(h, h.Length, throwOnEndOfStream: false);
            if (n >= 24 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47)
                return (BigEndian(h[16..]), BigEndian(h[20..]));
            if (n >= 10 && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46)
                return (h[6] | h[7] << 8, h[8] | h[9] << 8);
            if (n >= 26 && h[0] == 0x42 && h[1] == 0x4D)
                return (LittleEndian(h[18..]), Math.Abs(LittleEndian(h[22..])));
            if (n >= 30 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50)
            {
                if (h[12] == 0x56 && h[13] == 0x50 && h[14] == 0x38 && h[15] == 0x58) // VP8X
                    return (1 + (h[24] | h[25] << 8 | h[26] << 16), 1 + (h[27] | h[28] << 8 | h[29] << 16));
                if (h[12] == 0x56 && h[13] == 0x50 && h[14] == 0x38 && h[15] == 0x4C) // VP8L
                    return (1 + (((h[22] & 0x3F) << 8) | h[21]), 1 + (((h[24] & 0x0F) << 10) | (h[23] << 2) | ((h[22] & 0xC0) >> 6)));
                if (h[12] == 0x56 && h[13] == 0x50 && h[14] == 0x38 && h[15] == 0x20) // VP8
                    return ((h[26] | h[27] << 8) & 0x3FFF, (h[28] | h[29] << 8) & 0x3FFF);
                return null;
            }
            if (n >= 4 && h[0] == 0xFF && h[1] == 0xD8)
                return JpegSize(stream);
        }
        catch { /* unknown or broken header: decode in full */ }
        return null;
    }

    private static (int, int)? JpegSize(Stream s)
    {
        s.Position = 2;
        for (var guard = 0; guard < 4096; guard++)
        {
            var b = s.ReadByte();
            if (b < 0) return null;
            if (b != 0xFF) continue;
            int marker;
            do marker = s.ReadByte(); while (marker == 0xFF);
            if (marker < 0) return null;
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) continue; // markers without a length
            if (marker is 0xD9 or 0xDA) return null;                       // end, or scan data before any frame header
            var length = (s.ReadByte() << 8) | s.ReadByte();
            if (length < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                s.ReadByte(); // sample precision
                var height = (s.ReadByte() << 8) | s.ReadByte();
                var width = (s.ReadByte() << 8) | s.ReadByte();
                return width > 0 && height > 0 ? (width, height) : null;
            }
            s.Seek(length - 2, SeekOrigin.Current);
        }
        return null;
    }

    private static int BigEndian(ReadOnlySpan<byte> b) => b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3];
    private static int LittleEndian(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24;

    private static Control AlertCard(AlertBox box, Func<string, PartColor?> colors)
    {
        var part = colors(PartColorKeys.Alert(box.Id));
        var foreground = PartColorPainter.TryBrush(part?.Foreground) ?? Brush(ViewImage.Text);
        var panel = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(box.Title))
            panel.Children.Add(new TextBlock
            {
                Text = box.Title.Trim(),
                FontFamily = AppFonts.Thai,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground
            });
        if (!string.IsNullOrWhiteSpace(box.Text))
            panel.Children.Add(new TextBlock
            {
                Text = box.Text.Trim(),
                FontFamily = AppFonts.Thai,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 4,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = foreground
            });
        return new Border
        {
            Background = PartColorPainter.TryBrush(part?.Background) ?? Brush(ViewImage.CellFill),
            BorderBrush = Brush(ViewImage.GridLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            Child = panel
        };
    }

    private static ViewImageOptions WithRows(ViewImageOptions o, IReadOnlyList<int> rows) => new()
    {
        VisibleRows = rows,
        RespectHideDone = o.RespectHideDone,
        Now = o.Now,
        Zoom = o.Zoom,
        IsHidden = o.IsHidden,
        RowHeight = o.RowHeight,
        CellTextColors = o.CellTextColors,
        HeaderColors = o.HeaderColors,
        RowNumberColors = o.RowNumberColors,
        CellColors = o.CellColors,
        CellDetails = o.CellDetails
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
