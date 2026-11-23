using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.1.1 drawing and pictures on the table in layers, pictures in cells.
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}
var dir = Directory.CreateTempSubdirectory("tn-sketch").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
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

// ── layers ──
var ink = new InkDocument();
Ok(ink.ActiveLayer.Name == "เลเยอร์ 1" && ink.Layers.Count == 1, "the first layer is made when needed");
var l2 = InkOps.AddLayer(ink);
Ok(l2 != null && ink.Layers.Count == 2 && ink.Active == 1 && l2.Name == "เลเยอร์ 2", "+ Layer adds one above the active one and makes it active");
ink.Active = 0;
InkOps.AddLayer(ink, "  ลายมือ   หัวหน้า ");
Ok(ink.Layers[1].Name == "ลายมือ หัวหน้า" && ink.Active == 1 && ink.Layers[2].Name == "เลเยอร์ 2", "a named layer goes right above the active one; spaces tidied");
while (InkOps.AddLayer(ink) != null) { }
Ok(ink.Layers.Count == InkOps.MaxLayers, "at most 10 layers");
ink.Active = 3;
InkOps.DeleteActiveLayer(ink);
Ok(ink.Layers.Count == InkOps.MaxLayers - 1 && ink.Active == 2, "deleting a layer makes the one below active");
var single = new InkDocument();
single.ActiveLayer.Strokes.Add(new SketchStroke("#000000", 2, 100, [0, 0, 5, 5]));
var keepId = single.ActiveLayer.Id;
InkOps.DeleteActiveLayer(single);
Ok(single.Layers.Count == 1 && single.ActiveLayer.Strokes.Count == 0 && single.ActiveLayer.Id == keepId, "deleting the only layer empties it");

// ── pictures on a layer ──
Ok(InkOps.PlaceSizeFor(1600, 800) == (320, 160) && InkOps.PlaceSizeFor(100, 50) == (100, 50), "a new picture keeps its shape, at most 320 px");
var pic = new SketchImage("a", "x.png", 10, 20, 200, 100);
var layer = new InkLayer { Images = [pic, new SketchImage("b", "y.png", 100, 50, 50, 50)] };
Ok(InkOps.HitImage(layer, 120, 60) == 1 && InkOps.HitImage(layer, 20, 30) == 0 && InkOps.HitImage(layer, 500, 500) == -1, "the topmost picture under the point is picked");
Ok(InkOps.OnHandle(pic, 210, 120) && !InkOps.OnHandle(pic, 100, 60), "the resize handle is the bottom-right corner");
var bigger = InkOps.Resize(pic, 410, 140, free: false);
Ok(bigger.W == 400 && bigger.H == 200 && bigger.X == 10 && bigger.Y == 20, "resizing keeps the shape, following the longer drag", $"{bigger.W}x{bigger.H}");
var free = InkOps.Resize(pic, 110, 400, free: true);
Ok(free.W == 100 && free.H == 380, "with Shift any shape");
Ok(InkOps.Resize(pic, 0, 0, false).W >= InkOps.MinImageSize, "never smaller than 16 px");

// ── layers.json ──
var file = Path.Combine(dir, "layers.json");
Ok(InkStore.PathFor(note) == file && InkStore.ImagesDir(note) == Path.Combine(dir, "images"), "layers.json and images/ live next to note.json");
var d0 = InkStore.Load(file, out var w0);
Ok(w0 == null && d0.Layers.Count == 0 && d0.CellImages.Count == 0, "no file: no layers yet");
var d1 = new InkDocument { PenColor = "#DC2626", PenWidth = 7, Sharpness = 40, EraserWidth = 30 };
d1.ActiveLayer.Strokes.Add(new SketchStroke("#16A34A", 5, 35, [1.04, 2.06, 30, 40, 55, 60]));
InkOps.AddLayer(d1, "ภาพ");
d1.ActiveLayer.Visible = false;
d1.ActiveLayer.Images.Add(new SketchImage("img1", "img-1.png", 5, 6, 70, 80));
d1.ActiveLayer.Strokes.Add(new SketchStroke("#000000", 2, 100, [0, 0, 80, 80], ruler: true));
d1.CellImages[CellDetailOps.Key("r1", "c1")] = new CellImage("img-2.jpg", true);
InkStore.Save(file, d1);
InkStore.Save(file, d1);
var d2 = InkStore.Load(file, out var w2);
Ok(w2 == null && d2.Layers.Count == 2 && d2.Active == 1 && d2.PenColor == "#DC2626" && d2.PenWidth == 7 && d2.Sharpness == 40 && d2.EraserWidth == 30,
    "layers and settings come back");
Ok(d2.Layers[0].Visible && !d2.Layers[1].Visible && d2.Layers[1].Name == "ภาพ" && d2.Layers[0].Id == d1.Layers[0].Id, "names, ids and shown / hidden come back");
Ok(d2.Layers[0].Strokes.Single().Points.SequenceEqual([1.0, 2.1, 30, 40, 55, 60]) && d2.Layers[1].Strokes.Single().Ruler &&
   d2.Layers[1].Images.Single() == new SketchImage("img1", "img-1.png", 5, 6, 70, 80), "strokes (to 0.1 px) and pictures come back");
Ok(d2.CellImages.Single().Value == new CellImage("img-2.jpg", true), "pictures in cells come back");
Ok(File.Exists(file + ".prev") && !File.Exists(file + ".tmp"), "the file before is kept as layers.json.prev");
File.WriteAllText(file, "{ broken");
var d3 = InkStore.Load(file, out var w3);
Ok(w3 != null && w3.StartsWith("layers.json.bad-", StringComparison.Ordinal) && !File.Exists(file) && d3.Layers.Count == 0, "a broken file is kept aside", w3 ?? "");
File.WriteAllText(file, "{\"layers\":[{\"name\":\"\",\"images\":[{\"file\":\"../evil.png\",\"w\":5,\"h\":5},{\"file\":\"a.txt\"},{\"file\":\"ok.png\",\"w\":2,\"h\":2}]}]," +
                        "\"cellImages\":{\"nokey\":{\"file\":\"a.png\"},\"r|c\":{\"file\":\"..\\\\x.png\"}}}");
var d4 = InkStore.Load(file, out _);
Ok(d4.Layers[0].Name == "เลเยอร์ 1" && d4.Layers[0].Images.Single().File == "ok.png" && d4.Layers[0].Images[0].W == InkOps.MinImageSize && d4.CellImages.Count == 0,
    "pictures outside the images folder, other file types and bad cell keys are dropped",
    string.Join(",", d4.Layers[0].Images.Select(i => i.File)) + " / " + string.Join(",", d4.CellImages.Keys));
File.Delete(file);

// ── on the table ──
var seed = new NoteDocument { Columns = ["หัวข้อ", "รายละเอียด", "สถานะ"], ColumnRolesList = ["title", "", "status"], DueColumnOmitted = true, TrayNoticeShown = true };
for (var i = 0; i < 30; i++) seed.Rows.Add([$"งาน {i + 1}", "", ""]);
seed.EnsureRowIds();
seed.EnsureColumnIds();
NoteStore.Save(note, seed);
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();

// a picture to insert: 200 × 100, blue
var png = Path.Combine(dir, "blue.png");
{
    var wb = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
    using (var fb = wb.Lock())
        unsafe { for (var i = 0; i < 200 * 100; i++) ((uint*)fb.Address)[i] = 0xFF2563EB; }
    wb.Save(png);
}

var window = new MainWindow { Width = 980, Height = 640 };
ToolTip.SetServiceEnabled(window, false);
window.Show();
Pump(window, 300);
var overlay = window.InkLayerControl;
var draw = window.FindControl<Button>("SketchButton")!;
Ok(draw.Content as string == "วาดเขียน" && !overlay.IsHitTestVisible && !window.InkOn, "Draw button; the table takes the mouse while drawing is off");
Click(window, draw);
Pump(window, 50);
var bar = window.FindControl<Border>("InkBar")!;
Ok(window.InkOn && bar.IsVisible && overlay.IsHitTestVisible, "Draw turns drawing on: the bar shows and the layers take the mouse");
var barTexts = bar.GetLogicalDescendants().OfType<ContentControl>().Select(c => c.Content as string ?? "").Where(s => s.Length > 0)
    .Concat(bar.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")).ToList();
Ok(new[] { "ปากกา", "ยางลบ", "ไม้บรรทัด", "ย้าย", "ลบทั้งหมด", "+ รูปภาพ", "+ เลเยอร์", "เสร็จ", "ความคมชัด", "เลเยอร์ 1" }.All(barTexts.Contains),
    "Thai tool names, sharpness and the first layer in the bar", string.Join(" | ", barTexts.Distinct()));

Point At(double x, double y) => overlay.TranslatePoint(new Point(x, y), window)!.Value;
void Drag(double x0, double y0, double x1, double y1, RawInputModifiers mods = RawInputModifiers.None, int steps = 10)
{
    window.MouseMove(At(x0, y0), mods);
    window.MouseDown(At(x0, y0), MouseButton.Left, mods);
    for (var i = 1; i <= steps; i++) window.MouseMove(At(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps), mods);
    window.MouseUp(At(x1, y1), MouseButton.Left, mods);
    Pump(window, 10);
}

var cellsBefore = string.Join("|", Doc(window).Rows.Select(r => string.Join(",", r)));
window.SetInkColor("#DC2626");
window.SetInkSize(5);
window.SetInkSharpness(100);
Drag(150, 80, 450, 140);
var strokes = window.Ink.ActiveLayer.Strokes;
Ok(strokes.Count == 1 && strokes[0].Color == "#DC2626" && strokes[0].Width == 5 && strokes[0].PointCount >= 8, "the pen writes on the table (layer 1)");
Ok(string.Join("|", Doc(window).Rows.Select(r => string.Join(",", r))) == cellsBefore, "drawing does not touch the cells");

// scrolling: the drawing moves with the table
var rows = RowsScroll(window);
rows.Offset = new Vector(0, 120);
Pump(window, 30);
Drag(150, 80, 450, 80, steps: 4);
var second = window.Ink.ActiveLayer.Strokes.Last();
Ok(Math.Abs(second.Points[1] - 200) < 1, "points are kept in table coordinates (scrolled 120 px → y 80 is 200)", second.Points[1].ToString("0.#"));
rows.Offset = new Vector(0, 0);
Pump(window, 30);

window.SetInkTool(SketchTool.Ruler);
Drag(150, 300, 500, 312, RawInputModifiers.Shift, steps: 5);
var ruler = window.Ink.ActiveLayer.Strokes.Last();
Ok(ruler.Ruler && Math.Abs(ruler.Points[1] - ruler.Points[3]) < 0.01, "the ruler with Shift draws a level line");

window.SetInkTool(SketchTool.Eraser);
window.SetInkSize(20);
var n = window.Ink.ActiveLayer.Strokes.Count;
Drag(300, 280, 300, 330, steps: 8);
Ok(window.Ink.ActiveLayer.Strokes.Count == n + 1, "the eraser cuts the ruler line in two");
window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
Ok(window.Ink.ActiveLayer.Strokes.Count == n, "Ctrl+Z while drawing undoes the drawing (not the table)");
window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
Ok(window.Ink.ActiveLayer.Strokes.Count == n + 1, "Ctrl+Y redoes it");

// a second layer with a picture
window.AddInkLayer();
Ok(window.Ink.Layers.Count == 2 && window.Ink.Active == 1, "+ Layer makes layer 2 and draws on it");
Ok(window.InsertLayerImage(png), "+ Picture puts a picture on the layer", window.InkInfo);
var img = window.Ink.ActiveLayer.Images.Single();
Ok(img.W == 200 && img.H == 100 && File.Exists(InkStore.ImagePath(note, img.File)) && overlay.Tool == SketchTool.Move && overlay.Selected == img.Id,
    "the picture is copied into images/, keeps its size, and is picked for moving", $"{img.W}x{img.H} {img.File}");
var c = img.X + 50; var r = img.Y + 50;
Drag(c, r, c + 100, r + 60, steps: 5);
var moved = window.Ink.ActiveLayer.Images.Single();
Ok(Math.Abs(moved.X - (img.X + 100)) < 1 && Math.Abs(moved.Y - (img.Y + 60)) < 1, "Move drags the picture", $"{moved.X},{moved.Y}");
Drag(moved.X + moved.W, moved.Y + moved.H, moved.X + moved.W + 200, moved.Y + moved.H + 20, steps: 5);
var sized = window.Ink.ActiveLayer.Images.Single();
Ok(sized.W > 300 && Math.Abs(sized.W / sized.H - 2) < 0.02, "dragging the corner resizes it, same shape", $"{sized.W}x{sized.H}");
Shot(window, "ink-on-table");
var redAt = At(300, 110);

// layers on and off (layer 2 with the picture hidden first, so only layer 1 is checked)
window.SetLayerVisible(1, false);
window.SetLayerVisible(0, false);
Pump(window, 20);
Ok(!window.Ink.Layers[0].Visible && window.Ink.Layers[0].Strokes.Count > 0, "hiding layer 1 keeps its drawing");
var frameHidden = Shot(window, "ink-layer1-hidden");
window.SetLayerVisible(0, true);
Pump(window, 20);
var frameShown = Shot(window, "ink-layer1-shown");
if (frameHidden != null && frameShown != null)
{
    var hidden = Pixel(frameHidden, (int)redAt.X, (int)redAt.Y);
    var shown = Pixel(frameShown, (int)redAt.X, (int)redAt.Y);
    Ok(shown.R > 150 && shown.G < 120 && !(hidden.R > 150 && hidden.G < 120), "the red line shows only while its layer is on", $"shown {shown}, hidden {hidden}");
}
window.SetLayerVisible(1, true);
Pump(window, 20);
Shot(window, "ink-both-layers");

// Esc: done; the table works again, the layers stay
window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
Pump(window, 30);
Ok(!window.InkOn && !bar.IsVisible && !overlay.IsHitTestVisible && File.Exists(file), "Esc stops drawing and saves layers.json");
Ok(draw.Content as string == "วาดเขียน" && draw.Foreground is Avalonia.Media.ISolidColorBrush { Color.R: < 120 }, "the Draw button looks as before again", draw.Foreground?.ToString() ?? "null");
window.SetLayerVisible(1, false);
Ok(!window.Ink.Layers[1].Visible, "layers can be shown or hidden while drawing is off (View → Note layers)");

// ── a picture in a cell ──
Ok(window.SetCellImage(2, 1, png), "a picture goes into cell B3");
Pump(window, 50);
var toggle = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CellImageToggle");
var cellImage = window.GetVisualDescendants().OfType<Image>().FirstOrDefault(i => i.Name == "CellImage");
Ok(toggle?.Content as string == "ภาพ ▾" && cellImage != null && cellImage.Bounds.Height > 50, "the cell shows the picture with a toggle", toggle?.Content?.ToString() ?? "no toggle");
Shot(window, "cell-image-open");
Click(window, toggle!);
Pump(window, 50);
toggle = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CellImageToggle");
Ok(toggle?.Content as string == "ภาพ ▸" && !window.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "CellImage"),
    "the toggle hides the picture; the cell keeps it", toggle?.Content?.ToString() ?? "");
Shot(window, "cell-image-closed");
var key = CellDetailOps.Key(Doc(window).RowIdAt(2), Doc(window).ColumnIdAt(1));
Ok(window.Ink.CellImages.TryGetValue(key, out var ci) && !ci.Open, "open / closed is kept per cell");
Click(window, toggle!);
Pump(window, 50);
Ok(window.Ink.CellImages[key].Open, "and opens again");

window.SaveInkNow();
var saved = InkStore.Load(file, out _);
Ok(saved.Layers.Count == 2 && saved.Layers[0].Strokes.Count == n + 1 && saved.Layers[1].Images.Count == 1 && !saved.Layers[1].Visible &&
   saved.CellImages.ContainsKey(key), "layers, pictures and the cell picture are saved");
window.RemoveCellImage(2, 1);
Pump(window, 30);
Ok(!window.Ink.CellImages.ContainsKey(key) && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CellImageToggle"), "Remove picture takes it out of the cell");

Console.WriteLine(fail == 0 ? "SketchTests: all passed" : $"SketchTests: {fail} failed");
return fail;

static NoteDocument Doc(MainWindow w)
{
    var session = typeof(MainWindow).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
    return (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
}

static ScrollViewer RowsScroll(MainWindow w) =>
    w.FindControl<ItemsControl>("RowsList")!.GetVisualDescendants().OfType<ScrollViewer>().First();

static void Click(Window window, Control control)
{
    var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    window.MouseMove(p);
    window.MouseDown(p, MouseButton.Left);
    window.MouseUp(p, MouseButton.Left);
}

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
