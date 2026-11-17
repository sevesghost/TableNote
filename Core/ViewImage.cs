using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TableNote;

/// <summary>
/// Image export and print of the table as it is shown. This is a separate entry point from
/// <see cref="Exporter"/> (csv / txt / xlsx): it does not change that path.
/// The picture is an Avalonia visual rendered with <see cref="RenderTargetBitmap"/>.
/// Hidden columns are decided by <see cref="ViewImageOptions.IsHidden"/>, which defaults to
/// <see cref="DefaultIsHidden"/> (stored width &lt;= 0). That delegate has the same shape as
/// ColumnGroupOps.IsHidden(NoteDocument, int), so a merge can assign the method group directly.
/// </summary>
public static partial class ViewImage
{
    public const int RowLabelWidth = 44;
    public const double FontSize = 14;
    public const string HeaderFill = "#F6E08A";
    public const string CellFill = "#FFFDF6";
    public const string LabelFill = "#F8E7A4";
    public const string GridLine = "#E4D08A";
    public const string Text = "#3B3222";
    public const string DoneText = "#9A9080";
    public const string DoneFill = "#EFEADF";
    public const string OverdueFill = "#FFD0C8";
    public const string TodayFill = "#FFE9A8";
    public const string HighlightFill = "#F2B35E";
    public const string InvalidNumber = "#B03A2E";
    public const string PageFill = "#FFFFFF";

    private static readonly Dictionary<string, ISolidColorBrush> Paints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default hidden-column rule: a stored width of 0 (what a collapsed group writes into the grid).
    /// Replace with ColumnGroupOps.IsHidden when that type is on the branch — same signature.
    /// </summary>
    public static bool DefaultIsHidden(NoteDocument doc, int column) => EffectiveWidth(doc, column) <= 0;

    /// <summary>Marks that stack above and below a base letter, built without a Thai literal in source.</summary>
    private static string MarkProbe() => string.Concat(
        (char)0x0E01, (char)0x0E35, (char)0x0E48,
        (char)0x0E1B, (char)0x0E35, (char)0x0E48,
        (char)0x0E1B, (char)0x0E39, (char)0x0E48,
        (char)0x0E19, (char)0x0E33,
        (char)0x0E01, (char)0x0E35, (char)0x0E49);

    public static ViewImagePlan Plan(NoteDocument doc, ViewImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        options ??= new ViewImageOptions();
        _ = options.Zoom; // live UI scale; column widths below are the unscaled grid widths

        var columns = new List<int>();
        var widths = new List<int>();
        var headers = new List<string>();
        var headerFills = new List<string>();
        var headerText = new List<string>();
        var hidden = options.IsHidden ?? DefaultIsHidden;
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            if (hidden(doc, c)) continue;
            var w = EffectiveWidth(doc, c);
            if (w <= 0) continue;
            columns.Add(c);
            widths.Add(w);
            headers.Add(doc.Columns[c] ?? "");
            var fill = HeaderFill;
            var fg = Text;
            if (options.HeaderColors != null && options.HeaderColors.TryGetValue(c, out var hc) && hc != null)
            {
                if (RowStyleOps.HexFor(hc.Background) is { } bf) fill = bf;
                if (RowStyleOps.HexFor(hc.Text) is { } tf) fg = tf;
            }
            headerFills.Add(fill);
            headerText.Add(fg);
        }

        var statusCol = doc.StatusColumnIndex();
        var dueCol = doc.DueColumnIndex();
        var now = options.Now ?? DateTime.Now;
        IEnumerable<int> indexes = options.VisibleRows != null
            ? options.VisibleRows.Where(r => r >= 0 && r < doc.Rows.Count)
            : Enumerable.Range(0, doc.Rows.Count);
        if (options.VisibleRows == null && options.RespectHideDone && doc.HideDone && statusCol >= 0)
            indexes = indexes.Where(r => !TaskStatuses.IsDone(doc.GetCell(r, statusCol)));

        var minLine = MinLineHeight();
        var rows = new List<ViewImageRow>();
        foreach (var row in indexes)
            rows.Add(BuildRow(doc, row, columns, widths, statusCol, dueCol, now, options, minLine));

        if (rows.Count == 0)
        {
            var msg = Localizer.T(doc.Rows.Count == 0 ? "table.empty" : "table.all_hidden");
            var cells = new ViewImageCell[columns.Count];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = new ViewImageCell { Text = i == 0 ? msg : "", Fill = CellFill, Foreground = Text };
            rows.Add(new ViewImageRow { Label = "", LabelFill = LabelFill, Fill = CellFill, Cells = cells, Height = minLine });
        }

        var headerHeight = minLine;
        for (var i = 0; i < headers.Count; i++)
            headerHeight = Math.Max(headerHeight, LineHeight(headers[i], widths[i], bold: true, minLine));

        var totalWidth = RowLabelWidth + widths.Sum();
        var totalHeight = headerHeight + rows.Sum(r => r.Height);
        return new ViewImagePlan
        {
            RowLabelWidth = RowLabelWidth,
            ColumnWidths = widths.ToArray(),
            Headers = headers.ToArray(),
            HeaderFills = headerFills.ToArray(),
            HeaderForegrounds = headerText.ToArray(),
            Rows = rows.ToArray(),
            HeaderHeight = headerHeight,
            Width = Math.Max(1, totalWidth),
            Height = Math.Max(1, totalHeight)
        };
    }

    public static Control Build(ViewImagePlan plan)
    {
        var grid = new Grid { Width = plan.Width, Height = plan.Height, UseLayoutRounding = true, Background = Brush(CellFill) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(plan.RowLabelWidth)));
        foreach (var w in plan.ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(w)));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(plan.HeaderHeight)));
        foreach (var row in plan.Rows)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(row.Height)));

        AddCell(grid, 0, 0, "", LabelFill, Text, bold: false, center: true);
        for (var c = 0; c < plan.Headers.Length; c++)
        {
            var fill = c < plan.HeaderFills.Length ? plan.HeaderFills[c] : HeaderFill;
            var fg = c < plan.HeaderForegrounds.Length ? plan.HeaderForegrounds[c] : Text;
            AddCell(grid, 0, c + 1, plan.Headers[c], fill, fg, bold: true, center: false);
        }

        for (var r = 0; r < plan.Rows.Length; r++)
        {
            var row = plan.Rows[r];
            AddCell(grid, r + 1, 0, row.Label, row.LabelFill, Text, bold: false, center: true);
            for (var c = 0; c < row.Cells.Length; c++)
            {
                var cell = row.Cells[c];
                AddCell(grid, r + 1, c + 1, cell.Text, cell.Fill, cell.Foreground, bold: false, center: false, strike: cell.Strike, right: cell.RightAlign);
            }
        }
        return grid;
    }

    public static byte[] RenderPng(NoteDocument doc, ViewImageOptions? options = null) =>
        Render(Build(Plan(doc, options)), Plan(doc, options).Width, Plan(doc, options).Height, ViewImageFormat.Png);

    public static byte[] Render(NoteDocument doc, ViewImageFormat format, ViewImageOptions? options = null)
    {
        var plan = Plan(doc, options);
        return Render(Build(plan), plan.Width, plan.Height, format);
    }

    public static void Save(NoteDocument doc, string path, ViewImageFormat? format = null, ViewImageOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var bytes = Render(doc, format ?? FormatForPath(path), options);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* moved */ }
        }
    }

    /// <summary>First print page. See <see cref="RenderPrintPages"/> for the full page set.</summary>
    public static byte[] RenderPrintPng(NoteDocument doc, ViewImageOptions? options = null, ViewPrintSpec? spec = null) =>
        RenderPrintPages(doc, options, spec)[0];

    /// <summary>Windows shell print verb on a file (page PNG, or one multi-page PDF). Other systems return <see cref="ViewPrintKind.Unavailable"/> and do not throw.</summary>
    public static ViewPrintResult TryShellPrint(string pngPath)
    {
        if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
            return new ViewPrintResult(ViewPrintKind.Failed, "missing file");
        if (!OperatingSystem.IsWindows())
            return new ViewPrintResult(ViewPrintKind.Unavailable, null);
        try
        {
            Process.Start(new ProcessStartInfo(pngPath)
            {
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true
            });
            return new ViewPrintResult(ViewPrintKind.Sent, null);
        }
        catch (Exception ex)
        {
            return new ViewPrintResult(ViewPrintKind.Failed, ex.Message);
        }
    }

    public static ViewImageFormat FormatForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" ? ViewImageFormat.Jpeg : ViewImageFormat.Png;

    /// <summary>Stored column width. Missing entries use the same default the grid fills in. 0 means collapsed.</summary>
    public static int EffectiveWidth(NoteDocument doc, int index)
    {
        if (index < 0 || index >= doc.Columns.Count) return 0;
        if (index >= doc.ColumnWidths.Count) return TableLimits.DefaultColumnWidth;
        return doc.ColumnWidths[index];
    }

    /// <summary>
    /// PNG/JPEG of a control that is already on screen. Ancestor zoom is not applied:
    /// pass the table itself, not the zoom host. Whatever the grid has painted
    /// (fonts, widths, row colours, and later per-cell colours) is what is saved.
    /// </summary>
    public static byte[] Capture(Control visual, ViewImageFormat format = ViewImageFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(visual);
        int width, height;
        if (visual.Bounds.Width >= 1 && visual.Bounds.Height >= 1)
        {
            width = Math.Max(1, (int)Math.Ceiling(visual.Bounds.Width));
            height = Math.Max(1, (int)Math.Ceiling(visual.Bounds.Height));
            return Encode(visual, width, height, format);
        }
        visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = visual.DesiredSize;
        width = Math.Max(1, (int)Math.Ceiling(size.Width));
        height = Math.Max(1, (int)Math.Ceiling(size.Height));
        visual.Arrange(new Rect(0, 0, width, height));
        return Encode(visual, width, height, format);
    }

    /// <summary>Fits a captured table image to page width on a white page. The bitmap already has the live colours.</summary>
    public static byte[] PrintPage(byte[] tablePng, ViewPrintSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(tablePng);
        spec ??= new ViewPrintSpec();
        var stream = new MemoryStream(tablePng);
        var source = new Bitmap(stream);
        var contentW = Math.Max(1, spec.PageWidth - spec.Margin * 2);
        var scale = source.Size.Width <= 1 ? 1 : contentW / source.Size.Width;
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Width = Math.Max(1, source.Size.Width * scale),
            Height = Math.Max(1, source.Size.Height * scale),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var footer = new TextBlock
        {
            Text = Localizer.T("print.page", 1, 1),
            FontFamily = AppFonts.Thai,
            FontSize = 12,
            Foreground = Brush(Text),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var page = new Border
        {
            Background = Brush(PageFill),
            Width = spec.PageWidth,
            Padding = new Thickness(spec.Margin),
            Child = new StackPanel { Children = { image, footer } }
        };
        page.Measure(new Size(spec.PageWidth, double.PositiveInfinity));
        var height = Math.Max(1, page.DesiredSize.Height);
        page.Arrange(new Rect(0, 0, spec.PageWidth, height));
        var bytes = Encode(page, spec.PageWidth, height, ViewImageFormat.Png, spec.Dpi);
        source.Dispose();
        stream.Dispose();
        return bytes;
    }

    private static byte[] Render(Control visual, int width, int height, ViewImageFormat format)
    {
        visual.Measure(new Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        return Encode(visual, width, height, format);
    }

    private static byte[] Encode(Control visual, double width, double height, ViewImageFormat format, double dpi = 96)
    {
        // 96 dpi keeps image export at the same pixel size as before. Print passes a higher dpi.
        int pxW, pxH;
        if (Math.Abs(dpi - 96) < 0.01)
        {
            dpi = 96;
            pxW = Math.Max(1, (int)Math.Ceiling(width));
            pxH = Math.Max(1, (int)Math.Ceiling(height));
        }
        else
        {
            pxW = Math.Max(1, (int)Math.Round(width * dpi / 96.0));
            pxH = Math.Max(1, (int)Math.Round(height * dpi / 96.0));
        }
        var px = new PixelSize(pxW, pxH);
        using var bitmap = new RenderTargetBitmap(px, new Vector(dpi, dpi));
        bitmap.Render(visual);
        using var ms = new MemoryStream();
        if (format == ViewImageFormat.Jpeg)
            bitmap.Save(ms, new JpegBitmapEncoderOptions { Quality = 92 });
        else
            bitmap.Save(ms, new PngBitmapEncoderOptions());
        if (ms.Length > 0) return ms.ToArray();
        // Some hosts leave the stream empty; the file encoder still writes the PNG.
        var tmp = Path.Combine(Path.GetTempPath(), "tn-rtb-" + Guid.NewGuid().ToString("N") + (format == ViewImageFormat.Jpeg ? ".jpg" : ".png"));
        try
        {
            if (format == ViewImageFormat.Jpeg)
                bitmap.Save(tmp, new JpegBitmapEncoderOptions { Quality = 92 });
            else
                bitmap.Save(tmp, new PngBitmapEncoderOptions());
            var bytes = File.ReadAllBytes(tmp);
            if (bytes.Length == 0)
                throw new InvalidOperationException("RenderTargetBitmap encoded an empty image (" + bitmap.PixelSize + ")");
            return bytes;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp */ }
        }
    }

    private static ViewImageRow BuildRow(NoteDocument doc, int row, List<int> columns, List<int> widths, int statusCol, int dueCol, DateTime now, ViewImageOptions options, int minLine)
    {
        var status = statusCol >= 0 ? doc.GetCell(row, statusCol) : "";
        var done = statusCol >= 0 && TaskStatuses.IsDone(status);
        var labelFill = LabelFill;
        var cellFill = CellFill;
        if (done) labelFill = cellFill = DoneFill;
        else if (dueCol >= 0 && DueDates.TryParse(doc.GetCell(row, dueCol), out var due))
        {
            if (DueDates.IsOverdue(due, now)) labelFill = cellFill = OverdueFill;
            else if (DueDates.IsDueToday(due, now)) labelFill = cellFill = TodayFill;
        }
        var id = row < doc.RowIds.Count ? doc.RowIds[row] : "";
        if (id.Length > 0)
        {
            var style = RowStyleOps.Get(doc, id);
            if (RowStyleOps.HexFor(style.Color) is { } hex) cellFill = hex;
            if (style.Highlight) labelFill = HighlightFill;
        }
        if (options.RowNumberColors != null && options.RowNumberColors.TryGetValue(row, out var rowNumber)
            && RowStyleOps.HexFor(rowNumber) is { } rowNumberFill)
            labelFill = rowNumberFill;

        var cells = new ViewImageCell[columns.Count];
        var height = minLine;
        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var raw = doc.GetCell(row, c);
            var type = TypeOf(doc, c);
            var text = CellText(doc, row, c, raw, id, statusCol, dueCol, type);
            if (options.CellDetails != null && options.CellDetails.TryGetValue((row, c), out var lines) && lines != null)
            {
                var extra = string.Join("\n", lines.Where(s => !string.IsNullOrEmpty(s)));
                if (extra.Length > 0) text = text.Length == 0 ? extra : text + "\n" + extra;
            }
            var fill = cellFill;
            if (options.CellColors != null && options.CellColors.TryGetValue((row, c), out var specified))
                fill = RowStyleOps.HexFor(specified) ?? cellFill; // invalid values are ignored
            var fg = Text;
            if (done) fg = DoneText;
            else if (type == ColumnTypes.Number && raw.Trim().Length > 0 && !ColumnSettingsOps.IsNumber(raw)) fg = InvalidNumber;
            if (options.CellTextColors != null && options.CellTextColors.TryGetValue((row, c), out var textColor)
                && RowStyleOps.HexFor(textColor) is { } textFill)
                fg = textFill;
            cells[i] = new ViewImageCell { Text = text, Fill = fill, Foreground = fg, Strike = done, RightAlign = type == ColumnTypes.Number };
            height = Math.Max(height, LineHeight(text, widths[i], bold: false, minLine));
        }
        // Explicit row height from the layout (rowHeights, once that field exists). Null = auto, from the text.
        if (options.RowHeight?.Invoke(doc, row) is int laidOut && laidOut > 0)
            height = laidOut;
        return new ViewImageRow
        {
            Label = (row + 1).ToString(CultureInfo.InvariantCulture),
            LabelFill = labelFill,
            Fill = cellFill,
            Cells = cells,
            Height = height
        };
    }

    private static string CellText(NoteDocument doc, int row, int column, string raw, string rowId, int statusCol, int dueCol, string type)
    {
        if (column == statusCol) return TaskStatuses.Label(TaskStatuses.DisplayCode(raw));
        if (column == dueCol) return DueText(doc, raw, rowId);
        if (type == ColumnTypes.Check)
            return ColumnSettingsOps.IsChecked(raw) ? ColumnSettingsOps.CheckedValue : (string.IsNullOrWhiteSpace(raw) ? "" : raw);
        return raw;
    }

    private static string DueText(NoteDocument doc, string raw, string rowId)
    {
        doc.Recurrence.TryGetValue(rowId, out var rec);
        var repeat = Recurrence.IsActive(rec) ? Recurrence.Label(rec) : "";
        string text;
        if (string.IsNullOrWhiteSpace(raw)) text = "";
        else if (!DueDates.TryParse(raw, out var due)) text = raw.Trim() + " ⚠";
        else
        {
            text = due.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            if (DueDates.HasTime(raw)) text += " " + DueDates.FormatTime(due);
        }
        if (repeat.Length > 0) text = text.Length == 0 ? repeat : text + " · " + repeat;
        var alarms = rowId.Length > 0 ? ReminderPlan.OffsetsFor(doc, rowId).Count : 0;
        var alarm = alarms > 0 ? "⏰" + alarms.ToString(CultureInfo.InvariantCulture) : "⏰";
        return text.Length == 0 ? alarm : text + " " + alarm;
    }

    private static string TypeOf(NoteDocument doc, int column)
    {
        if (column < 0 || column >= doc.Columns.Count) return ColumnTypes.Text;
        if (column == doc.StatusColumnIndex() || column == doc.DueColumnIndex()) return ColumnTypes.Text;
        if (column >= doc.ColumnIds.Count) return ColumnTypes.Text;
        return doc.ColumnSettings.TryGetValue(doc.ColumnIds[column], out var s) && s != null
            ? ColumnTypes.Normalize(s.Type)
            : ColumnTypes.Text;
    }

    private static int MinLineHeight()
    {
        var ft = Format(MarkProbe(), 4096, bold: false);
        return Math.Max(44, (int)Math.Ceiling(InkHeight(ft)) + 16);
    }

    private static int LineHeight(string text, int columnWidth, bool bold, int minLine)
    {
        if (string.IsNullOrEmpty(text) || (text.Length < 48 && !text.Contains('\n') && !text.Contains('\r')))
            return minLine;
        var ft = Format(text, columnWidth, bold);
        return Math.Max(minLine, (int)Math.Ceiling(InkHeight(ft)) + 16);
    }

    /// <summary>Height of the drawn glyphs, including marks that sit above the em box.</summary>
    public static double InkHeight(FormattedText ft)
    {
        var geo = ft.BuildGeometry(new Point(0, 0));
        if (geo == null) return Math.Max(ft.Height, ft.Extent);
        var b = geo.Bounds;
        return Math.Max(ft.Height, b.Bottom - Math.Min(0, b.Y));
    }

    public static double InkTop(FormattedText ft)
    {
        var geo = ft.BuildGeometry(new Point(0, 0));
        if (geo == null) return 0;
        return Math.Max(0, -geo.Bounds.Y);
    }

    private static FormattedText Format(string text, int columnWidth, bool bold)
    {
        var ft = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(AppFonts.Thai, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal),
            FontSize, Brush(Text));
        ft.MaxTextWidth = Math.Max(8, columnWidth - 12);
        return ft;
    }

    private static void AddCell(Grid grid, int row, int column, string text, string fill, string foreground, bool bold, bool center, bool strike = false, bool right = false)
    {
        var box = new Border
        {
            Background = Brush(fill),
            BorderBrush = Brush(GridLine),
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            Child = new ViewImageText
            {
                Text = text ?? "",
                Family = AppFonts.Thai,
                Weight = bold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brush(foreground),
                Center = center,
                RightAlign = right,
                Strike = strike
            }
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, column);
        grid.Children.Add(box);
    }

    private static ISolidColorBrush Brush(string hex)
    {
        var key = RowStyleOps.HexFor(hex) ?? CellFill;
        if (!Paints.TryGetValue(key, out var brush))
            Paints[key] = brush = new SolidColorBrush(Color.Parse(key));
        return brush;
    }
}

public enum ViewImageFormat { Png, Jpeg }

public enum ViewPrintKind { Sent, Unavailable, Failed }

public readonly record struct ViewPrintResult(ViewPrintKind Kind, string? Detail);

public sealed class ViewPrintSpec
{
    public const double LayoutDpi = 96;
    /// <summary>Print raster density. Image export stays at <see cref="LayoutDpi"/>.</summary>
    public const double DefaultDpi = 200;
    /// <summary>A4 width in pixels at <see cref="DefaultDpi"/> (210 mm).</summary>
    public const int A4WidthPx = 1654;
    /// <summary>A4 height in pixels at <see cref="DefaultDpi"/> (297 mm).</summary>
    public const int A4HeightPx = 2339;
    /// <summary>Band under the table reserved for the page number, in DIPs.</summary>
    public const double FooterBand = 22;

    /// <summary>Page width in DIPs. The default covers <see cref="A4WidthPx"/> pixels at <see cref="DefaultDpi"/>.</summary>
    public double PageWidth { get; init; } = A4WidthPx * LayoutDpi / DefaultDpi;
    /// <summary>Page height in DIPs. The default covers <see cref="A4HeightPx"/> pixels at <see cref="DefaultDpi"/>.</summary>
    public double PageHeight { get; init; } = A4HeightPx * LayoutDpi / DefaultDpi;
    public double Margin { get; init; } = 36;
    public double Dpi { get; init; } = DefaultDpi;

    public int PixelWidth => Math.Max(1, (int)Math.Round(PageWidth * Dpi / LayoutDpi));
    public int PixelHeight => Math.Max(1, (int)Math.Round(PageHeight * Dpi / LayoutDpi));
    public double ContentWidth => Math.Max(1, PageWidth - Margin * 2);
    public double ContentHeight => Math.Max(1, PageHeight - Margin * 2 - FooterBand);
}

public sealed class ViewImageOptions
{
    public bool RespectHideDone { get; init; } = true;
    /// <summary>Row indexes on screen, in order. When set, hide-done is not applied again.</summary>
    public IReadOnlyList<int>? VisibleRows { get; init; }
    public DateTime? Now { get; init; }
    /// <summary>Accepted and ignored. Callers pass the live zoom so a 80–200% view does not resize the file.</summary>
    public double Zoom { get; init; } = 1;
    /// <summary>
    /// Column left out of the picture. Null uses <see cref="ViewImage.DefaultIsHidden"/> (width &lt;= 0).
    /// Assign ColumnGroupOps.IsHidden here when column groups are merged; do not call it from this assembly until then.
    /// </summary>
    public Func<NoteDocument, int, bool>? IsHidden { get; init; }
    /// <summary>
    /// Laid-out pixel height of a data row, or null for auto (the text's own height).
    /// When rowHeights exists, set this to that row's pixel height. Values are not clamped here.
    /// </summary>
    public Func<NoteDocument, int, int?>? RowHeight { get; init; }
    /// <summary>Per-cell foreground (#RRGGBB or a palette key). Null keeps the usual text colour.</summary>
    public IReadOnlyDictionary<(int Row, int Column), string?>? CellTextColors { get; init; }
    /// <summary>Per-column header background and text. Null keeps #F6E08A / the usual text colour. Hidden columns are not painted.</summary>
    public IReadOnlyDictionary<int, ViewHeaderColor?>? HeaderColors { get; init; }
    /// <summary>Per-row row-number background. Null keeps the status or highlight colour.</summary>
    public IReadOnlyDictionary<int, string?>? RowNumberColors { get; init; }
    /// <summary>Per-cell fill, palette key or #RRGGBB. Invalid values are skipped.</summary>
    public IReadOnlyDictionary<(int Row, int Column), string?>? CellColors { get; init; }
    /// <summary>Extra lines under a cell (detail popup text, once that feature passes it in).</summary>
    public IReadOnlyDictionary<(int Row, int Column), IReadOnlyList<string>>? CellDetails { get; init; }
}

public sealed class ViewImagePlan
{
    public int RowLabelWidth { get; init; }
    public int[] ColumnWidths { get; init; } = [];
    public string[] Headers { get; init; } = [];
    public string[] HeaderFills { get; init; } = [];
    public string[] HeaderForegrounds { get; init; } = [];
    public ViewImageRow[] Rows { get; init; } = [];
    public int HeaderHeight { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public ViewRect CellRect(int row, int column)
    {
        var left = column < 0 ? 0 : RowLabelWidth + ColumnWidths.Take(column).Sum();
        var width = column < 0 ? RowLabelWidth : ColumnWidths[column];
        var top = row < 0 ? 0 : HeaderHeight + Rows.Take(row).Sum(r => r.Height);
        var height = row < 0 ? HeaderHeight : Rows[row].Height;
        return new ViewRect(left, top, left + width, top + height);
    }
}

public readonly record struct ViewRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public sealed class ViewImageRow
{
    public string Label { get; init; } = "";
    public string LabelFill { get; init; } = ViewImage.LabelFill;
    public string Fill { get; init; } = ViewImage.CellFill;
    public ViewImageCell[] Cells { get; init; } = [];
    public int Height { get; init; }
}

public sealed class ViewHeaderColor
{
    public string? Background { get; init; }
    public string? Text { get; init; }
}

public sealed class ViewImageCell
{
    public string Text { get; init; } = "";
    public string Fill { get; init; } = ViewImage.CellFill;
    public string Foreground { get; init; } = ViewImage.Text;
    public bool Strike { get; init; }
    public bool RightAlign { get; init; }
}

/// <summary>Draws one cell with <see cref="AppFonts.Thai"/>, leaving room for marks above and below the em box.</summary>
public sealed class ViewImageText : Control
{
    public string Text { get; set; } = "";
    public FontFamily Family { get; set; } = AppFonts.Thai;
    public FontWeight Weight { get; set; } = FontWeight.Normal;
    public IBrush? Foreground { get; set; }
    public bool Center { get; set; }
    public bool RightAlign { get; set; }
    public bool Strike { get; set; }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || Bounds.Width < 2 || Bounds.Height < 2) return;
        var ft = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyle.Normal, Weight), ViewImage.FontSize, Foreground ?? Brushes.Black);
        ft.MaxTextWidth = Math.Max(4, Bounds.Width - 12);
        if (Strike) ft.SetTextDecorations(TextDecorations.Strikethrough);
        var above = ViewImage.InkTop(ft);
        var block = ViewImage.InkHeight(ft);
        var y = Math.Max(above, (Bounds.Height - block) / 2 + above);
        var x = Center ? (Bounds.Width - ft.Width) / 2
            : RightAlign ? Math.Max(6, Bounds.Width - 6 - ft.Width)
            : 6;
        context.DrawText(ft, new Point(Math.Max(4, x), y));
    }
}
