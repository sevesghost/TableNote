using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using TableNote;

AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-viewimage.log");
// RenderTargetBitmap.Save needs the Skia desktop renderer (headless encodes an empty file).
AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();

int pass = 0, fail = 0;
void Ok(string name, bool ok, string detail = "")
{
    if (ok) pass++; else fail++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
}

static SKColor Hex(string h) => new(Convert.ToByte(h[1..3], 16), Convert.ToByte(h[3..5], 16), Convert.ToByte(h[5..7], 16));
static SKBitmap Decode(byte[] png)
{
    var head = BitConverter.ToString(png.Take(Math.Min(24, png.Length)).ToArray());
    try
    {
        var bmp = SKBitmap.Decode(png);
        if (bmp == null) throw new InvalidOperationException("null bitmap");
        return bmp;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"decode failed len={png.Length} head={head} ({ex.GetType().Name}: {ex.Message})", ex);
    }
}
static SKColor At(SKBitmap bmp, int x, int y) => bmp.GetPixel(Math.Clamp(x, 0, bmp.Width - 1), Math.Clamp(y, 0, bmp.Height - 1));
static bool Same(SKColor c, string hex)
{
    var e = Hex(hex);
    return c.Red == e.Red && c.Green == e.Green && c.Blue == e.Blue;
}
static bool Any(SKBitmap bmp, string hex)
{
    var e = Hex(hex);
    for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Red == e.Red && c.Green == e.Green && c.Blue == e.Blue) return true;
        }
    return false;
}
static int LooseInk(string sample)
{
    var host = new Avalonia.Controls.Border
    {
        Width = 280,
        Height = 140,
        Background = Avalonia.Media.Brushes.White,
        Child = new ViewImageText { Text = sample, Family = AppFonts.Thai, Foreground = Avalonia.Media.Brushes.Black }
    };
    host.Measure(new Size(280, 140));
    host.Arrange(new Rect(0, 0, 280, 140));
    using var rtb = new RenderTargetBitmap(new PixelSize(280, 140), new Vector(96, 96));
    rtb.Render(host);
    using var ms = new MemoryStream();
    rtb.Save(ms, new PngBitmapEncoderOptions());
    using var bmp = Decode(ms.ToArray());
    int minY = int.MaxValue, maxY = int.MinValue;
    for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Red > 250 && c.Green > 250 && c.Blue > 250) continue;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
    return maxY >= minY ? maxY - minY + 1 : 0;
}
static (int MinY, int MaxY, int Count) Ink(SKBitmap bmp, ViewRect cell, string fill, string grid)
{
    var fillC = Hex(fill);
    var gridC = Hex(grid);
    int minY = int.MaxValue, maxY = int.MinValue, n = 0;
    for (var y = cell.Top + 1; y < cell.Bottom - 1; y++)
        for (var x = cell.Left + 1; x < cell.Right - 1; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 200) continue;
            if (c.Red == fillC.Red && c.Green == fillC.Green && c.Blue == fillC.Blue) continue;
            if (c.Red == gridC.Red && c.Green == gridC.Green && c.Blue == gridC.Blue) continue;
            n++;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
    return (minY, maxY, n);
}

NoteDocument Doc()
{
    Localizer.SetLanguage("en");
    var d = NoteDocument.CreateDefault();
    d.Rows.Clear();
    d.RowIds.Clear();
    return d;
}
void Row(NoteDocument d, params string[] cells)
{
    d.Rows.Add(cells.ToList());
    d.RowIds.Add(Guid.NewGuid().ToString("N"));
}

void OnUi(Action action) => Dispatcher.UIThread.Invoke(action);

OnUi(() =>
{
    Ok("version stays 5", NoteDocument.CurrentVersion == 5);

    var widths = Doc();
    Row(widths, "", "", "", "");
    widths.SetColumnWidth(0, 72);
    widths.SetColumnWidth(1, 140);
    widths.SetColumnWidth(2, 420);
    var plan = ViewImage.Plan(widths);
    Ok("widths 72/140/420", plan.ColumnWidths.SequenceEqual(new[] { 72, 140, 420, widths.ColumnWidths[3] }),
        string.Join(",", plan.ColumnWidths));
    var png = ViewImage.Render(widths, ViewImageFormat.Png);
    using var bmp = Decode(png);
    Ok("image width is the sum of columns", bmp.Width == plan.Width && plan.Width == 44 + 72 + 140 + 420 + widths.ColumnWidths[3],
        $"{bmp.Width} vs {plan.Width}");
    var last = plan.CellRect(0, plan.ColumnWidths.Length - 1);
    Ok("wide table reaches the right edge", last.Right == bmp.Width && bmp.Width > 700, $"right {last.Right} bmp {bmp.Width}");
    Ok("last column interior keeps its fill", Same(At(bmp, last.Left + last.Width / 2, last.Top + last.Height / 2), ViewImage.CellFill));

    var zoomA = ViewImage.Render(widths, ViewImageFormat.Png, new ViewImageOptions { Zoom = 0.8, Now = new DateTime(2026, 6, 15, 9, 0, 0) });
    var zoomB = ViewImage.Render(widths, ViewImageFormat.Png, new ViewImageOptions { Zoom = 2.0, Now = new DateTime(2026, 6, 15, 9, 0, 0) });
    Ok("zoom does not change output size", zoomA.Length == zoomB.Length && zoomA.AsSpan().SequenceEqual(zoomB));

    var colors = Doc();
    Row(colors, "", "", "", "");
    Row(colors, "", "", "", "");
    Row(colors, "", "", "", "");
    colors.RowStyles[colors.RowIds[0]] = new RowStyle { Color = "red" };
    colors.RowStyles[colors.RowIds[1]] = new RowStyle { Color = "#112233" };
    colors.RowStyles[colors.RowIds[2]] = new RowStyle { Color = "nope" };
    byte[] colorPng = [];
    Exception? crashed = null;
    try { colorPng = ViewImage.Render(colors, ViewImageFormat.Png, new ViewImageOptions { Now = new DateTime(2026, 1, 1) }); }
    catch (Exception ex) { crashed = ex; }
    Ok("invalid color does not throw", crashed == null, crashed?.Message ?? "");
    using var cb = Decode(colorPng);
    var p0 = ViewImage.Plan(colors, new ViewImageOptions { Now = new DateTime(2026, 1, 1) });
    var r0 = p0.CellRect(0, 0);
    var r1 = p0.CellRect(1, 0);
    var r2 = p0.CellRect(2, 0);
    Ok("palette red is #F9C9C2", Same(At(cb, r0.Left + 8, r0.Top + r0.Height / 2), "#F9C9C2"));
    Ok("custom #112233 is exact", Same(At(cb, r1.Left + 8, r1.Top + r1.Height / 2), "#112233"));
    Ok("invalid color falls back", Same(At(cb, r2.Left + 8, r2.Top + r2.Height / 2), ViewImage.CellFill));
    foreach (var (key, _, hex) in RowStyleOps.Palette)
    {
        var one = Doc();
        Row(one, "", "", "", "");
        one.RowStyles[one.RowIds[0]] = new RowStyle { Color = key };
        var pp = ViewImage.Plan(one, new ViewImageOptions { Now = new DateTime(2026, 1, 1) });
        using var pb = Decode(ViewImage.Render(one, ViewImageFormat.Png, new ViewImageOptions { Now = new DateTime(2026, 1, 1) }));
        var rc = pp.CellRect(0, 1);
        Ok("palette " + key, Same(At(pb, rc.Left + rc.Width / 2, rc.Top + rc.Height / 2), hex));
    }

    var hide = Doc();
    Row(hide, "keep", "", "todo", "");
    Row(hide, "gone", "", "done", "");
    Row(hide, "also", "", "", "");
    hide.HideDone = true;
    hide.RowStyles[hide.RowIds[1]] = new RowStyle { Color = "#FF00AA" };
    hide.RowStyles[hide.RowIds[0]] = new RowStyle { Color = "#00AA00" };
    var hiddenPlan = ViewImage.Plan(hide);
    Ok("hide done drops the completed row", hiddenPlan.Rows.Length == 2 && hiddenPlan.Rows.All(r => !r.Cells.Any(c => c.Text == "gone")));
    using var hb = Decode(ViewImage.Render(hide, ViewImageFormat.Png));
    Ok("hidden row color is not painted", !Any(hb, "#FF00AA"));
    Ok("visible row color is painted", Any(hb, "#00AA00"));

    var lang = Doc();
    Row(lang, "task", "", "", "");
    Localizer.SetLanguage("en");
    var en = ViewImage.Plan(lang);
    Localizer.SetLanguage("th");
    var th = ViewImage.Plan(lang);
    Localizer.SetLanguage("en");
    var status = lang.StatusColumnIndex();
    var shown = en.ColumnWidths.ToList();
    int statusSlot = 0, seen = 0;
    for (var c = 0; c < lang.Columns.Count; c++)
    {
        if (ViewImage.DefaultIsHidden(lang, c)) continue;
        if (c == status) { statusSlot = seen; break; }
        seen++;
    }
    Ok("status label follows English", en.Rows[0].Cells[statusSlot].Text == "Not started", en.Rows[0].Cells[statusSlot].Text);
    Ok("status label follows Thai", th.Rows[0].Cells[statusSlot].Text == "ยังไม่เริ่ม", th.Rows[0].Cells[statusSlot].Text);

    var thai = Doc();
    thai.SetColumnWidth(0, 420);
    Row(thai, "กี่ปี่", "", "", "");
    Row(thai, "ปู่", "", "", "");
    Row(thai, "น้ำ", "", "", "");
    var tp = ViewImage.Plan(thai);
    var visual = ViewImage.Build(tp);
    var drawn = Walk(visual).OfType<ViewImageText>().First(t => t.Text == "น้ำ");
    Ok("export text uses AppFonts.Thai", ReferenceEquals(drawn.Family, AppFonts.Thai));
    using var tb = Decode(ViewImage.Render(thai, ViewImageFormat.Png));
    foreach (var sample in new[] { "กี่ปี่", "ปู่", "น้ำ" })
    {
        var slot = 0;
        var rect = tp.CellRect(slot, 0);
        // find the row whose text is the sample
        for (var i = 0; i < tp.Rows.Length; i++)
            if (tp.Rows[i].Cells[0].Text == sample) rect = tp.CellRect(i, 0);
        var ink = Ink(tb, rect, ViewImage.CellFill, ViewImage.GridLine);
        var loose = LooseInk(sample);
        var cellH = ink.MaxY - ink.MinY + 1;
        Ok($"Thai \"{sample}\" is drawn and not clipped",
            ink.Count > 8 && loose > 8 && ink.MinY > rect.Top + 1 && ink.MaxY < rect.Bottom - 2 && Math.Abs(cellH - loose) <= 2,
            $"cell ink {ink.MinY}-{ink.MaxY} ({cellH}px) loose {loose}px cell {rect.Top}-{rect.Bottom}");
    }

    var collapsed = Doc();
    Row(collapsed, "A", "HIDDEN", "C", "D");
    collapsed.ColumnWidths[1] = 0;
    var cp = ViewImage.Plan(collapsed);
    Ok("width 0 column is skipped", cp.Headers.Length == 3 && !cp.Rows[0].Cells.Any(c => c.Text == "HIDDEN"),
        string.Join(",", cp.ColumnWidths));
    var hooked = ViewImage.Plan(collapsed, new ViewImageOptions
    {
        IsHidden = (_, col) => col == 0
    });
    Ok("IsHidden hook drops a column the merger marks",
        hooked.Rows[0].Cells.All(c => c.Text != "A") && hooked.ColumnWidths.Length == collapsed.Columns.Count - 2,
        string.Join("|", hooked.Rows[0].Cells.Select(c => c.Text)));

    var tinted = Doc();
    Row(tinted, "Aa", "", "", "");
    var usual = ViewImage.Plan(tinted, new ViewImageOptions { Now = new DateTime(2026, 1, 1) });
    Ok("colour hooks left null keep the usual header and text",
        usual.HeaderFills.Length == usual.Headers.Length && usual.HeaderFills.All(h => h == ViewImage.HeaderFill)
        && usual.Rows[0].LabelFill == ViewImage.LabelFill && usual.Rows[0].Cells[0].Foreground == ViewImage.Text);
    var tint = new ViewImageOptions
    {
        Now = new DateTime(2026, 1, 1),
        CellTextColors = new Dictionary<(int, int), string?> { [(0, 0)] = "#AA00AA" },
        HeaderColors = new Dictionary<int, ViewHeaderColor?> { [0] = new() { Background = "#112233", Text = "#FFFFFF" }, [1] = new() { Background = "#FF00AA" } },
        RowNumberColors = new Dictionary<int, string?> { [0] = "#00BB77" },
        IsHidden = (_, col) => col == 1
    };
    var tintedPlan = ViewImage.Plan(tinted, tint);
    Ok("CellTextColors, HeaderColors and RowNumberColors are used",
        tintedPlan.Rows[0].Cells[0].Foreground == "#AA00AA"
        && tintedPlan.HeaderFills[0] == "#112233" && tintedPlan.HeaderForegrounds[0] == "#FFFFFF"
        && tintedPlan.Rows[0].LabelFill == "#00BB77");
    Ok("header paint skips the hidden column", tintedPlan.HeaderFills.All(h => h != "#FF00AA") && !tintedPlan.Headers.Contains(tinted.Columns[1]));
    using (var tintBmp = Decode(ViewImage.Render(tinted, ViewImageFormat.Png, tint)))
    {
        var head = tintedPlan.CellRect(-1, 0);
        var num = tintedPlan.CellRect(0, -1);
        Ok("header and row-number colours are painted, hidden header colour is not",
            Same(At(tintBmp, head.Left + head.Width / 2, head.Top + head.Height / 2), "#112233")
            && Same(At(tintBmp, num.Left + 4, num.Top + num.Height / 2), "#00BB77")
            && !Any(tintBmp, "#FF00AA"));
    }

    var v4path = Path.Combine(Path.GetTempPath(), "tn-v4-" + Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllText(v4path, """
        {"version":4,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["กี่ปี่","ปู่","todo","2026-05-01"]],"rowIds":["r1"],"columnRoles":["title","detail","status","due"]}
        """);
    var loaded = NoteStore.Load(v4path);
    byte[] v4png = [];
    Exception? v4ex = null;
    try { v4png = ViewImage.Render(loaded.Document, ViewImageFormat.Png); }
    catch (Exception ex) { v4ex = ex; }
    Ok("v4 file without widths or colors exports", v4ex == null && v4png.Length > 32, v4ex?.Message ?? "");
    var v4plan = ViewImage.Plan(loaded.Document);
    Ok("v4 widths follow load defaults", v4plan.Width == 44 + loaded.Document.ColumnWidths.Sum(),
        string.Join(",", loaded.Document.ColumnWidths));

    var csvDoc = Doc();
    Row(csvDoc, "hello", "x", "todo", "2026-05-01");
    var cols = new ExportColumns(csvDoc.StatusColumnIndex(), csvDoc.DueColumnIndex());
    var (headers, rows) = Exporter.FromDocument(csvDoc);
    var csv = Path.Combine(Path.GetTempPath(), "tn-csv-" + Guid.NewGuid().ToString("N") + ".csv");
    Exporter.Export(rows, headers, csv, ExportFormat.Csv, cols);
    var csvBytes = File.ReadAllBytes(csv);
    var csvText = System.Text.Encoding.UTF8.GetString(csvBytes);
    Ok("existing csv export still writes a BOM and the status label",
        csvBytes.Length > 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF && csvText.Contains("Not started"));
    ViewImage.Render(csvDoc, ViewImageFormat.Png);
    Ok("image export does not change the csv file", File.ReadAllBytes(csv).AsSpan().SequenceEqual(csvBytes));

    var jpg = ViewImage.Render(widths, ViewImageFormat.Jpeg);
    Ok("jpeg export starts with the JPEG marker", jpg.Length > 4 && jpg[0] == 0xFF && jpg[1] == 0xD8);
    using (var jb = Decode(jpg))
        Ok("jpeg has the same pixel size as the png", jb.Width == bmp.Width && jb.Height == bmp.Height);

    var many = Doc();
    for (var i = 0; i < 1000; i++) Row(many, "r" + i, "x", "", "");
    var sw = Stopwatch.StartNew();
    var big = ViewImage.Render(many, ViewImageFormat.Png, new ViewImageOptions { Now = new DateTime(2026, 1, 1) });
    sw.Stop();
    using var bb = Decode(big);
    Ok($"1000 rows export in {sw.ElapsedMilliseconds} ms", sw.ElapsedMilliseconds < 15000 && bb.Height > 1000 * 20, $"{bb.Width}x{bb.Height}");

    var printDoc = Doc();
    printDoc.SetColumnWidth(0, 420);
    printDoc.SetColumnWidth(1, 420);
    printDoc.SetColumnWidth(2, 420);
    printDoc.SetColumnWidth(3, 420);
    Row(printDoc, "", "", "", "");
    printDoc.RowStyles[printDoc.RowIds[0]] = new RowStyle { Color = "#010203" };
    var print = ViewImage.RenderPrintPng(printDoc, new ViewImageOptions { Now = new DateTime(2026, 1, 1) });
    using var pbmp = Decode(print);
    Ok("print page is page width", pbmp.Width == 794, pbmp.Width.ToString());
    Ok("print margin is white", Same(At(pbmp, 2, 2), "#FFFFFF"));
    Ok("print keeps the row color and the wide table", Any(pbmp, "#010203"));
    var shell = ViewImage.TryShellPrint(Path.Combine(Path.GetTempPath(), "missing-table.png"));
    Ok("missing print file fails cleanly", shell.Kind == ViewPrintKind.Failed);
    var real = Path.Combine(Path.GetTempPath(), "tn-print-" + Guid.NewGuid().ToString("N") + ".png");
    File.WriteAllBytes(real, print);
    var attempt = ViewImage.TryShellPrint(real);
    Ok("non-Windows print does not throw", attempt.Kind is ViewPrintKind.Unavailable or ViewPrintKind.Sent, attempt.Kind.ToString());

    var swatch = new Avalonia.Controls.Border
    {
        Width = 120,
        Height = 48,
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#112233")),
        Child = new Avalonia.Controls.TextBlock
        {
            Text = "กี่ปี่",
            FontFamily = AppFonts.Thai,
            Foreground = Avalonia.Media.Brushes.White,
            Margin = new Thickness(8)
        }
    };
    var live = ViewImage.Capture(swatch);
    using (var lb = Decode(live))
        Ok("RenderTargetBitmap keeps a colour painted on the control", Same(At(lb, 6, 6), "#112233") && lb.Width == 120);
    var livePage = ViewImage.PrintPage(live);
    using (var lp = Decode(livePage))
    {
        Ok("print of a live capture fits the page width", lp.Width == 794, lp.Width.ToString());
        Ok("print of a live capture keeps that colour", Any(lp, "#112233"));
    }

    var art = "/opt/cursor/artifacts";
    try
    {
        Directory.CreateDirectory(art);
        var demo = Doc();
        demo.SetColumnWidth(0, 72);
        demo.SetColumnWidth(1, 140);
        demo.SetColumnWidth(2, 420);
        Row(demo, "กี่ปี่", "ปู่", "", "2026-06-01 09:30");
        Row(demo, "น้ำ", "รายละเอียด", "", "2026-06-15");
        demo.Rows[0][2] = "todo";
        demo.Rows[1][2] = "";
        demo.RowStyles[demo.RowIds[0]] = new RowStyle { Color = "yellow" };
        demo.RowStyles[demo.RowIds[1]] = new RowStyle { Color = "#7EB6FF" };
        var shownGrid = ViewImage.Build(ViewImage.Plan(demo, new ViewImageOptions { Now = new DateTime(2026, 6, 1) }));
        var captured = ViewImage.Capture(shownGrid);
        File.WriteAllBytes(Path.Combine(art, "tablenote-export.png"), captured);
        var page = ViewImage.PrintPage(captured);
        var pagePath = Path.Combine(art, "tablenote-print-page.png");
        File.WriteAllBytes(pagePath, page);
        var win = new ViewPrintWindow(pagePath);
        win.Show();
        Dispatcher.UIThread.RunJobs();
        var size = new PixelSize(960, 720);
        using var shot = new RenderTargetBitmap(size, new Vector(96, 96));
        shot.Render(win);
        shot.Save(Path.Combine(art, "tablenote-print-preview.png"));
        win.Close();
        Ok("wrote export and print-preview screenshots", File.Exists(Path.Combine(art, "tablenote-print-preview.png")));
    }
    catch (Exception ex)
    {
        Ok("wrote export and print-preview screenshots", false, ex.Message);
    }
});

Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

static IEnumerable<Avalonia.Controls.Control> Walk(Avalonia.Controls.Control c)
{
    yield return c;
    if (c is Avalonia.Controls.Panel panel)
        foreach (var child in panel.Children)
            if (child is Avalonia.Controls.Control cc)
                foreach (var d in Walk(cc)) yield return d;
    if (c is Avalonia.Controls.Decorator decorator && decorator.Child is Avalonia.Controls.Control dc)
        foreach (var d in Walk(dc)) yield return d;
}
