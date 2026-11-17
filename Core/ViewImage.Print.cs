using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

public static partial class ViewImage
{
    /// <summary>
    /// One printed page of rows. A row that fits on a page is never split.
    /// <see cref="ViewPrintSlice.Clipped"/> is only set when that single row is taller than the page body.
    /// </summary>
    public static IReadOnlyList<ViewPrintSlice> PlanPages(ViewImagePlan plan, ViewPrintSpec? spec = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        spec ??= new ViewPrintSpec();
        if (plan.Rows.Length == 0)
            return [new ViewPrintSlice()];

        var usable = UsableRowDip(plan, spec);
        var pages = new List<ViewPrintSlice>();
        var start = 0;
        var used = 0.0;
        var i = 0;
        while (i < plan.Rows.Length)
        {
            var h = Math.Max(1, plan.Rows[i].Height);
            if (h > usable)
            {
                if (used > 0)
                {
                    pages.Add(new ViewPrintSlice { Start = start, Count = i - start });
                    start = i;
                    used = 0;
                }
                var chunk = Math.Max(1, (int)Math.Floor(usable));
                var offset = 0;
                while (offset < h)
                {
                    var take = Math.Min(chunk, h - offset);
                    pages.Add(new ViewPrintSlice
                    {
                        Start = i,
                        Count = 1,
                        Clipped = true,
                        ClipOffset = offset,
                        ClipHeight = take
                    });
                    offset += take;
                }
                i++;
                start = i;
                used = 0;
                continue;
            }
            if (used > 0 && used + h > usable)
            {
                pages.Add(new ViewPrintSlice { Start = start, Count = i - start });
                start = i;
                used = 0;
                continue;
            }
            used += h;
            i++;
        }
        if (start < plan.Rows.Length)
            pages.Add(new ViewPrintSlice { Start = start, Count = plan.Rows.Length - start });
        if (pages.Count == 0)
            pages.Add(new ViewPrintSlice());
        return pages;
    }

    /// <summary>
    /// Prints on row boundaries and repeats the column header on every page.
    /// A wide table is scaled down to the A4 content width. Columns are not split.
    /// Pages are rasterized at <see cref="ViewPrintSpec.Dpi"/> (200 by default).
    /// </summary>
    public static IReadOnlyList<byte[]> RenderPrintPages(NoteDocument doc, ViewImageOptions? options = null, ViewPrintSpec? spec = null)
    {
        spec ??= new ViewPrintSpec();
        var plan = Plan(doc, options);
        var slices = PlanPages(plan, spec);
        var list = new List<byte[]>(slices.Count);
        for (var i = 0; i < slices.Count; i++)
            list.Add(RenderPrintSlice(plan, slices[i], spec, i, slices.Count));
        return list;
    }

    /// <summary>How the table fits the page width. Narrower tables stay at layout size.</summary>
    public static double PageScale(ViewImagePlan plan, ViewPrintSpec spec)
    {
        var contentW = spec.ContentWidth;
        return plan.Width > contentW ? contentW / plan.Width : 1;
    }

    private static double UsableRowDip(ViewImagePlan plan, ViewPrintSpec spec)
    {
        var scale = PageScale(plan, spec);
        // One DIP of slack so layout rounding cannot push the page number off the page.
        var usable = spec.ContentHeight / scale - plan.HeaderHeight - 1;
        return usable < 1 ? 1 : usable;
    }

    private static byte[] RenderPrintSlice(ViewImagePlan plan, ViewPrintSlice slice, ViewPrintSpec spec, int index, int count)
    {
        var scale = PageScale(plan, spec);
        var table = BuildPrintTable(plan, slice);
        Control body = table;
        if (scale < 0.999)
        {
            body = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = table,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
        }
        var footer = new TextBlock
        {
            Text = Localizer.T("print.page", index + 1, count),
            FontFamily = AppFonts.Thai,
            FontSize = 12,
            Foreground = Brush(Text),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var stack = new StackPanel
        {
            Width = spec.ContentWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { body, footer }
        };
        var page = new Border
        {
            Background = Brush(PageFill),
            Width = spec.PageWidth,
            Height = spec.PageHeight,
            Padding = new Thickness(spec.Margin),
            ClipToBounds = true,
            Child = stack
        };
        page.Measure(new Size(spec.PageWidth, spec.PageHeight));
        page.Arrange(new Rect(0, 0, spec.PageWidth, spec.PageHeight));
        return Encode(page, spec.PageWidth, spec.PageHeight, ViewImageFormat.Png, spec.Dpi);
    }

    private static Control BuildPrintTable(ViewImagePlan plan, ViewPrintSlice slice)
    {
        var dataHeight = slice.Clipped
            ? Math.Max(1, slice.ClipHeight)
            : DataHeight(plan, slice);
        var grid = new Grid
        {
            Width = plan.Width,
            Height = plan.HeaderHeight + dataHeight,
            UseLayoutRounding = true,
            Background = Brush(CellFill)
        };
        AddColumns(grid, plan);
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(plan.HeaderHeight)));
        PaintHeader(grid, plan);
        if (slice.Clipped)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(dataHeight)));
            var row = plan.Rows[slice.Start];
            var inner = new Grid
            {
                Width = plan.Width,
                Height = row.Height,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -slice.ClipOffset, 0, 0)
            };
            AddColumns(inner, plan);
            inner.RowDefinitions.Add(new RowDefinition(new GridLength(row.Height)));
            PaintDataRow(inner, plan, slice.Start, 0);
            var clip = new Border
            {
                ClipToBounds = true,
                Width = plan.Width,
                Height = dataHeight,
                Child = inner
            };
            Grid.SetRow(clip, 1);
            Grid.SetColumnSpan(clip, plan.ColumnWidths.Length + 1);
            grid.Children.Add(clip);
            return grid;
        }
        for (var i = 0; i < slice.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(plan.Rows[slice.Start + i].Height)));
        for (var i = 0; i < slice.Count; i++)
            PaintDataRow(grid, plan, slice.Start + i, i + 1);
        return grid;
    }

    private static int DataHeight(ViewImagePlan plan, ViewPrintSlice slice)
    {
        var h = 0;
        for (var i = 0; i < slice.Count; i++)
            h += plan.Rows[slice.Start + i].Height;
        return h;
    }

    private static void AddColumns(Grid grid, ViewImagePlan plan)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(plan.RowLabelWidth)));
        foreach (var w in plan.ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(w)));
    }

    private static void PaintHeader(Grid grid, ViewImagePlan plan)
    {
        AddCell(grid, 0, 0, "", LabelFill, Text, bold: false, center: true);
        for (var c = 0; c < plan.Headers.Length; c++)
        {
            var fill = c < plan.HeaderFills.Length ? plan.HeaderFills[c] : HeaderFill;
            var fg = c < plan.HeaderForegrounds.Length ? plan.HeaderForegrounds[c] : Text;
            AddCell(grid, 0, c + 1, plan.Headers[c], fill, fg, bold: true, center: false);
        }
    }

    private static void PaintDataRow(Grid grid, ViewImagePlan plan, int rowIndex, int gridRow)
    {
        var row = plan.Rows[rowIndex];
        AddCell(grid, gridRow, 0, row.Label, row.LabelFill, Text, bold: false, center: true);
        for (var c = 0; c < row.Cells.Length; c++)
        {
            var cell = row.Cells[c];
            AddCell(grid, gridRow, c + 1, cell.Text, cell.Fill, cell.Foreground, bold: false, center: false, strike: cell.Strike, right: cell.RightAlign);
        }
    }
}

/// <summary>Rows placed on one print page. <see cref="Clipped"/> means one over-tall row was sliced.</summary>
public sealed class ViewPrintSlice
{
    public int Start { get; init; }
    public int Count { get; init; }
    public bool Clipped { get; init; }
    public int ClipOffset { get; init; }
    public int ClipHeight { get; init; }
}
