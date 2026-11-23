using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TableNote;

// 2.6.1.1 drawing pad.
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}
var dir = Directory.CreateTempSubdirectory("tn-sketch").FullName;
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");
Localizer.SetLanguage("th");

// ── colours, sizes, sharpness ──
Ok(SketchOps.ParseColor("#2563eb") == "#2563EB" && SketchOps.ParseColor("f00") == "#FF0000" && SketchOps.ParseColor("blue") == null &&
   SketchOps.ParseColor("#12345") == null, "colours are read as #RRGGBB");
Ok(SketchOps.ClampSharpness(-5) == 0 && SketchOps.ClampSharpness(150) == 100, "sharpness stays within 0–100");
Ok(SketchOps.ClampWidth(0) == SketchOps.MinWidth && SketchOps.ClampWidth(999) == SketchOps.MaxWidth, "size stays within its range");
Ok(SketchOps.Layers(4, 100) is [(4, 1)], "sharpness 100 is one crisp line");
var soft = SketchOps.Layers(4, 0);
Ok(soft.Count > 1 && soft[0].Width > 4 && soft[^1].Width < 4 && soft.All(l => l.Opacity > 0), "sharpness 0 is a soft, wider edge around a thinner core, still visible");
var mid = SketchOps.Layers(4, 50);
Ok(mid[0].Width < soft[0].Width && mid[0].Width > 4, "sharpness 50 lies between", $"{mid[0].Width} vs {soft[0].Width}");

// ── ruler ──
var (rx, ry) = SketchOps.RulerEnd(0, 0, 100, 8, snap: true);
Ok(Math.Abs(ry) < 1e-6 && Math.Abs(rx - Math.Sqrt(100 * 100 + 64)) < 1e-3, "Shift: nearly level becomes level, same length", $"{rx},{ry}");
var (dx, dy) = SketchOps.RulerEnd(0, 0, 50, 47, snap: true);
Ok(Math.Abs(dx - dy) < 1e-3, "Shift: nearly diagonal becomes 45°", $"{dx},{dy}");
Ok(SketchOps.RulerEnd(0, 0, 50, 47, snap: false) == (50, 47), "without Shift the end is where it was dragged");
Ok(SketchOps.RulerAngle(0, 0, 10, 0) == 0 && SketchOps.RulerAngle(0, 0, 0, -10) == 90 && SketchOps.RulerAngle(0, 0, -10, 0) == 180 &&
   SketchOps.RulerAngle(0, 0, 0, 10) == 270, "ruler angle like a protractor (up is 90°)");

// ── eraser ──
var line = new SketchStroke("#000000", 2, 100, [0, 0, 100, 0]);
var other = new SketchStroke("#FF0000", 2, 100, [0, 50, 100, 50]);
var after = SketchOps.Erase([line, other], 50, -10, 50, 10, 5);
Ok(after is { Count: 3 }, "erasing across the middle of a line splits it in two", after?.Count.ToString() ?? "null");
Ok(after != null && ReferenceEquals(after[^1], other), "a stroke the eraser does not touch is kept as it is");
Ok(after != null && after[0].Points[^2] < 50 - 5 && after[1].Points[0] > 50 + 5 && after[0].Color == "#000000" && after[1].Width == 2,
    "the pieces keep colour and size and leave a gap where the eraser went");
Ok(SketchOps.Erase([line, other], 50, 200, 60, 200, 5) == null, "nothing touched: null (no undo step)");
Ok(SketchOps.Erase([line], -20, 0, 120, 0, 5) is { Count: 0 }, "erasing along the whole line removes it");
var dot = new SketchStroke("#000000", 4, 100, [10, 10]);
Ok(SketchOps.Erase([dot], 11, 11, 11, 11, 3) is { Count: 0 }, "a dot can be erased");
var ruled = new SketchStroke("#000000", 2, 100, [0, 0, 300, 0], ruler: true);
var ruledLeft = SketchOps.Erase([ruled], 150, 0, 150, 0, 10);
Ok(ruledLeft is { Count: 2 } && ruledLeft.All(s => s.Ruler), "a ruler line (two points) is erased in the middle too");
Ok(SketchOps.Erase([new SketchStroke("#000000", 20, 100, [0, 0, 100, 0])], 50, 14, 50, 14, 5) is { Count: 2 },
    "a thick line is hit at its edge, not only at its centre");

// ── sketch.json ──
var file = Path.Combine(dir, "sketch.json");
var doc0 = SketchStore.Load(file, out var w0);
Ok(w0 == null && doc0.Pages.Count == 1 && doc0.Current.Strokes.Count == 0 && doc0.Lined, "no file: one empty lined page");
doc0.PenColor = "#DC2626"; doc0.PenWidth = 7; doc0.Sharpness = 40; doc0.EraserWidth = 30; doc0.Lined = false;
doc0.Current.Strokes.Add(new SketchStroke("#16A34A", 5, 35, [1.04, 2.06, 30, 40, 55, 60]));
doc0.Pages.Add(new SketchPage { Strokes = [new SketchStroke("#000000", 2, 100, [0, 0, 80, 80], ruler: true)] });
doc0.Page = 1;
SketchStore.Save(file, doc0);
SketchStore.Save(file, doc0); // second save swaps in and keeps .prev
var doc1 = SketchStore.Load(file, out var w1);
Ok(w1 == null && doc1.Pages.Count == 2 && doc1.Page == 1 && !doc1.Lined && doc1.PenColor == "#DC2626" && doc1.PenWidth == 7 &&
   doc1.Sharpness == 40 && doc1.EraserWidth == 30, "settings and pages come back");
var s0 = doc1.Pages[0].Strokes.Single();
Ok(s0.Color == "#16A34A" && s0.Width == 5 && s0.Sharpness == 35 && s0.Points.SequenceEqual([1.0, 2.1, 30, 40, 55, 60]) && !s0.Ruler &&
   doc1.Pages[1].Strokes.Single().Ruler, "strokes come back (points to 0.1 px), ruler lines marked");
Ok(File.Exists(file + ".prev") && !File.Exists(file + ".tmp"), "the file before is kept as sketch.json.prev, no .tmp left");
File.WriteAllText(file, "{ not json");
var doc2 = SketchStore.Load(file, out var w2);
Ok(w2 != null && w2.StartsWith("sketch.json.bad-", StringComparison.Ordinal) && File.Exists(Path.Combine(dir, w2)) && !File.Exists(file) &&
   doc2.Current.Strokes.Count == 0, "a broken file is kept aside and the pad starts empty", w2 ?? "");
File.WriteAllText(file, "{\"version\":1,\"page\":9,\"penWidth\":-3,\"sharpness\":400,\"penColor\":\"nope\",\"pages\":[{\"strokes\":[{\"color\":\"#000\",\"width\":2,\"points\":[1]}," +
                        "{\"color\":\"#000\",\"width\":2,\"points\":[1,2,3,4]}]}]}");
var doc3 = SketchStore.Load(file, out _);
Ok(doc3.Page == 0 && doc3.PenWidth == SketchOps.MinWidth && doc3.Sharpness == 100 && doc3.PenColor == SketchOps.DefaultColor &&
   doc3.Current.Strokes.Count == 1 && doc3.Current.Strokes[0].Color == "#000000", "odd values are put right, a stroke without a point is dropped");
Ok(SketchStore.PathFor(Path.Combine(dir, "note.json")) == file, "sketch.json lives next to note.json");
File.Delete(file);

// ── the window ──
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();
var pad = new SketchWindow(file) { Width = 980, Height = 680 };
pad.Show();
Pump(pad, 50);
var labels = pad.GetLogicalDescendants().OfType<ContentControl>().Select(c => c.Content as string ?? "").Where(s => s.Length > 0).ToList();
Ok(new[] { "ปากกา", "ยางลบ", "ไม้บรรทัด", "ลบทั้งหมด", "กระดาษมีเส้น", "+ หน้า", "ลบหน้า" }.All(labels.Contains), "Thai tool names", string.Join(" | ", labels));
var texts = pad.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
Ok(texts.Contains("ความคมชัด") && texts.Contains("สี") && texts.Contains("ขนาด") && texts.Contains("หน้า 1/1"), "colour, size, sharpness and page labels", string.Join(" | ", texts));

var canvas = pad.Canvas;
Point At(double x, double y) => canvas.TranslatePoint(new Point(x, y), pad)!.Value;
void Drag(double x0, double y0, double x1, double y1, RawInputModifiers mods = RawInputModifiers.None, int steps = 12)
{
    pad.MouseMove(At(x0, y0), mods);
    pad.MouseDown(At(x0, y0), MouseButton.Left, mods);
    for (var i = 1; i <= steps; i++) pad.MouseMove(At(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps), mods);
    pad.MouseUp(At(x1, y1), MouseButton.Left, mods);
    Pump(pad, 10);
}

pad.SetColor("#2563EB");
pad.SetSize(6);
pad.SetSharpness(100);
Drag(100, 100, 400, 160);
var strokes = pad.Document.Current.Strokes;
Ok(strokes.Count == 1 && strokes[0].Color == "#2563EB" && strokes[0].Width == 6 && strokes[0].Sharpness == 100 && strokes[0].PointCount >= 10 && !strokes[0].Ruler,
    "the pen draws a stroke with the chosen colour, size and sharpness", strokes.Count == 0 ? "none" : $"{strokes[0].Color} {strokes[0].Width} {strokes[0].PointCount}");
Ok(pad.CanUndo, "a stroke is an undo step");

pad.SetSharpness(20);
pad.SetTool(SketchTool.Ruler);
Drag(100, 300, 500, 318, RawInputModifiers.Shift, steps: 6);
var ruler = pad.Document.Current.Strokes.Last();
Ok(ruler.Ruler && ruler.PointCount == 2 && Math.Abs(ruler.Points[1] - ruler.Points[3]) < 0.01 && ruler.Sharpness == 20,
    "the ruler with Shift draws a level straight line", string.Join(",", ruler.Points.Select(v => v.ToString("0.#"))));
pad.SetTool(SketchTool.Ruler);
Drag(100, 400, 300, 470, steps: 4);
var free = pad.Document.Current.Strokes.Last();
Ok(free.Ruler && Math.Abs(free.Points[2] - 300) < 0.5 && Math.Abs(free.Points[3] - 470) < 0.5, "without Shift the ruler goes where it is dragged");

pad.SetTool(SketchTool.Eraser);
pad.SetSize(24);
Ok(pad.Document.EraserWidth == 24 && pad.Document.PenWidth == 6, "the eraser keeps its own size");
var before = pad.Document.Current.Strokes.Count;
Drag(300, 250, 300, 340, steps: 10); // across the level ruler line
Ok(pad.Document.Current.Strokes.Count == before + 1 && pad.Document.Current.Strokes.Count(s => s.Ruler && Math.Abs(s.Points[1] - 300) < 1) == 2,
    "the eraser cuts the ruler line in two", pad.Document.Current.Strokes.Count.ToString());
pad.Undo();
Ok(pad.Document.Current.Strokes.Count == before && pad.CanRedo, "Undo brings the erased part back");
pad.Redo();
Ok(pad.Document.Current.Strokes.Count == before + 1, "Redo erases it again");
pad.SetTool(SketchTool.Pen);
Ok(pad.Document.Current.Strokes.Count > 0, "still drawn before Clear all");
Shot(pad, "sketch-drawn");

pad.AddPage();
Ok(pad.Document.Pages.Count == 2 && pad.Document.Page == 1 && pad.Document.Current.Strokes.Count == 0 &&
   pad.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "หน้า 2/2"), "+ Page adds an empty page after this one and shows it");
Drag(80, 80, 200, 200);
pad.GoToPage(0);
Ok(pad.Document.Current.Strokes.Count == before + 1, "the first page keeps its drawing");

pad.ClearAllNow();
Ok(pad.Document.Current.Strokes.Count == 0 && pad.Document.Pages[1].Strokes.Count == 1, "Clear all empties this page only");
pad.Undo();
Ok(pad.Document.Current.Strokes.Count == before + 1, "Undo brings a cleared page back");

// Clear all through the button asks first.
var clearButton = pad.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ลบทั้งหมด");
clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(pad, 80);
var ask = pad.OwnedWindows.FirstOrDefault(w => w.IsVisible);
Ok(ask != null, "Clear all asks first");
if (ask != null)
{
    ask.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ยกเลิก").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(pad, 80);
}
Ok(pad.Document.Current.Strokes.Count == before + 1, "Cancel keeps the drawing");

pad.SetHex("#7c3aed");
Ok(pad.Document.PenColor == "#7C3AED", "a colour typed as #RRGGBB is used");
pad.SetHex("purple");
Ok(pad.Document.PenColor == "#7C3AED" && pad.StatusText.Contains("#RRGGBB"), "a colour that can't be read is refused with a hint", pad.StatusText);

// Keys
pad.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
Ok(pad.Canvas.Tool == SketchTool.Eraser, "E picks the eraser");
pad.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
Ok(pad.Canvas.Tool == SketchTool.Ruler, "R picks the ruler");
pad.KeyPress(Key.P, RawInputModifiers.None, PhysicalKey.P, "p");
Ok(pad.Canvas.Tool == SketchTool.Pen, "P picks the pen");
var n = pad.Document.Current.Strokes.Count;
Drag(600, 100, 700, 150);
pad.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
Ok(pad.Document.Current.Strokes.Count == n, "Ctrl+Z undoes the last stroke");
pad.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
Ok(pad.Document.Current.Strokes.Count == n + 1, "Ctrl+Y redoes it");

pad.Close();
Pump(pad, 30);
var saved = SketchStore.Load(file, out _);
Ok(saved.Pages.Count == 2 && saved.Pages[0].Strokes.Count == n + 1 && saved.Pages[1].Strokes.Count == 1 && saved.PenColor == "#7C3AED",
    "closing the window saves sketch.json", $"{saved.Pages.Count} pages, {saved.Pages[0].Strokes.Count} strokes");

var again = new SketchWindow(file);
again.Show();
Pump(again, 30);
Ok(again.Document.Current.Strokes.Count == n + 1 && !again.CanUndo, "opening again shows the drawing (undo starts fresh)");
again.DeletePage();
Ok(again.Document.Pages.Count == 1, "Delete page removes it");
again.DeletePage();
Ok(again.Document.Pages.Count == 1 && again.Document.Current.Strokes.Count == 0, "deleting the last page leaves one empty page");
again.Undo(); again.Undo();
Ok(again.Document.Pages.Count == 2, "page deletes can be undone");
again.Close();

// Soft lines (sharpness 0) render and look different from crisp ones.
var softPad = new SketchWindow(Path.Combine(dir, "soft.json")) { Width = 700, Height = 420 };
softPad.Show();
Pump(softPad, 20);
softPad.SetSize(10);
softPad.SetSharpness(0);
softPad.Canvas.Document.Lined = false;
var c2 = softPad.Canvas;
Point At2(double x, double y) => c2.TranslatePoint(new Point(x, y), softPad)!.Value;
softPad.MouseDown(At2(60, 100), MouseButton.Left);
for (var i = 1; i <= 10; i++) softPad.MouseMove(At2(60 + 50 * i, 100));
softPad.MouseUp(At2(560, 100), MouseButton.Left);
softPad.SetSharpness(100);
softPad.MouseDown(At2(60, 200), MouseButton.Left);
for (var i = 1; i <= 10; i++) softPad.MouseMove(At2(60 + 50 * i, 200));
softPad.MouseUp(At2(560, 200), MouseButton.Left);
Pump(softPad, 20);
var frame = Shot(softPad, "sketch-soft-vs-crisp");
if (frame != null)
{
    int Ink(double y0)
    {
        var count = 0;
        for (var y = (int)y0 - 20; y <= y0 + 20; y++)
        {
            var p = c2.TranslatePoint(new Point(300, y), softPad)!.Value;
            var px = Pixel(frame, (int)p.X, (int)p.Y);
            if (px.R < 245 || px.G < 245) count++;
        }
        return count;
    }
    var softInk = Ink(100);
    var crispInk = Ink(200);
    Ok(softInk > crispInk && crispInk >= 8, "a soft line spreads wider than a crisp one of the same size", $"soft {softInk} px, crisp {crispInk} px");
}
softPad.Close();

Console.WriteLine(fail == 0 ? "SketchTests: all passed" : $"SketchTests: {fail} failed");
return fail;

static void Pump(Window w, int ms)
{
    var sw = Stopwatch.StartNew();
    do { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(1); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
}

static Avalonia.Media.Imaging.WriteableBitmap? Shot(Window w, string name)
{
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
    var frame = w.CaptureRenderedFrame() ?? w.GetLastRenderedFrame();
    if (frame == null) return null;
    var outDir = Environment.GetEnvironmentVariable("TN_SHOTS");
    if (!string.IsNullOrEmpty(outDir)) { Directory.CreateDirectory(outDir); frame.Save(Path.Combine(outDir, name + ".png")); }
    using var ms = new MemoryStream();
    frame.Save(ms);
    ms.Position = 0;
    return Avalonia.Media.Imaging.WriteableBitmap.Decode(ms);
}

static Avalonia.Media.Color Pixel(Avalonia.Media.Imaging.WriteableBitmap bmp, int x, int y)
{
    using var fb = bmp.Lock();
    unsafe
    {
        var p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
        return fb.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Avalonia.Media.Color.FromArgb(p[3], p[0], p[1], p[2])
            : Avalonia.Media.Color.FromArgb(p[3], p[2], p[1], p[0]);
    }
}
