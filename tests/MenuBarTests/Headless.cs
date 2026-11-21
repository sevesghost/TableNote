using System.Text;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

/// <summary>Opens the real window headless, in Thai, and checks the menu bar against the live selection.</summary>
static class MenuBarHeadless
{
    public static void Run(Action<string, bool, string> ok)
    {
        var dir = Directory.CreateTempSubdirectory("tn-menu-shot").FullName;
        var note = Path.Combine(dir, "note.json");
        Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
        AppLog.PathOverride = Path.Combine(dir, "tablenote.log");

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(AppFonts.ManagerOptions) // as Program.BuildAvaloniaApp
            .SetupWithoutStarting();

        var window = new MainWindow { Width = 980, Height = 640 };
        window.Show();
        Pump(window);

        var menu = window.FindControl<Menu>("MainMenu") ?? throw new Exception("menu bar missing");
        var titles = Headers(menu.Items);
        ok("thai menu titles",
            titles.SequenceEqual(["ไฟล์", "ตาราง", "คอลัมน์", "แถว", "มุมมอง", "ช่วยเหลือ"]),
            string.Join(" | ", titles));
        // 2.6.0: one row of everyday commands, no group labels; the rest is in the menu bar.
        var tools = window.FindControl<WrapPanel>("EditTools") ?? throw new Exception("toolbar missing");
        var commands = tools.Children.Where(c => c.IsVisible && c is Button or CheckBox).ToList();
        var labels0 = commands.Select(c => c is ContentControl cc ? cc.Content?.ToString() ?? "" : "").ToList();
        ok("toolbar has the everyday commands in order",
            labels0.SequenceEqual(["+ แถว", "− แถว", "+ คอลัมน์", "สี", "ซ่อนงานเสร็จ", "เรียงตามกำหนด", "A−", "100%", "A+", "ส่งออก", "วอลเปเปอร์"]),
            string.Join(" | ", labels0));
        var middles = commands.Select(c => c.Bounds.Center.Y).ToList();
        ok("toolbar is one row in a 980 px window",
            middles.Max() - middles.Min() < 8,
            string.Join(",", middles.Select(y => y.ToString("0"))));
        ok("no toolbar group labels any more", window.FindControl<TextBlock>("GroupTableLabel") == null, "");
        ok("no empty alert panel", window.FindControl<Border>("AlertHost")?.IsVisible == false, "");
        // 2.6.0: Thai, digits, Latin and symbols in one font (no fallback to Segoe UI / DejaVu for "17/11", "A+", "✓").
        var foreign = ForeignRuns([window]);
        ok("every text in the Thai window uses the bundled font", foreign.Count == 0 && TextRunCount([window]) > 20,
            string.Join(" | ", foreign.Take(8)) + $" ({TextRunCount([window])} runs)");

        var table = Item(menu, "ตาราง");
        table.Open();
        Pump(window);
        var spots = table.Items.OfType<MenuItem>().Where(i => i.Tag is string t && t.StartsWith("spot:", StringComparison.Ordinal)).ToList();
        ok("alert-date and status spots are present and disabled",
            spots.Count == 2 && spots.All(s => s.IsEnabled == false) &&
            spots[0].Header?.ToString() == "เพิ่มคอลัมน์วันที่แจ้งเตือน" &&
            spots[1].Header?.ToString() == "เพิ่มคอลัมน์สถานะ",
            string.Join(" | ", spots.Select(s => $"{s.Header} enabled={s.IsEnabled}")));
        Save(window, "menu-table-th");
        var menuForeign = ForeignRuns(table.Items.OfType<MenuItem>());
        ok("menu items use the bundled font too", menuForeign.Count == 0 && TextRunCount(table.Items.OfType<MenuItem>()) > 5,
            string.Join(" | ", menuForeign.Take(8)));
        table.Close();
        Pump(window);

        Save(window, "toolbar-menu-th-wide");

        window.Width = 480;
        Pump(window);
        var scroll = window.FindControl<ScrollViewer>("ToolScroll") ?? throw new Exception("toolbar scroll missing");
        ok("toolbar stays within its band", scroll.Bounds.Height <= 97, scroll.Bounds.Height.ToString("0"));
        ok("narrow window keeps add-row inside the toolbar", Fits(scroll, window.FindControl<Button>("AddRowButton")), "");
        ok("narrow window wraps the toolbar and keeps the wallpaper button",
            Fits(scroll, window.FindControl<Button>("WallpaperButton")) && Fits(scroll, window.FindControl<Button>("ExportButton")),
            scroll.Extent.Height.ToString("0"));
        Save(window, "toolbar-menu-th-narrow");

        window.Width = 980;
        Pump(window);
        var cell = window.FindControl<ItemsControl>("RowsList")!
            .GetVisualDescendants().OfType<TextBox>().First();
        Click(window, cell);
        Pump(window);

        var column = Item(menu, "คอลัมน์");
        column.Open();
        Pump(window);
        Save(window, "menu-column-th");
        var labels = Headers(column.Items);
        ok("column menu lists cut, moves, settings, group, delete",
            labels.Contains("ตัด") && labels.Contains("ย้ายไปทางซ้าย") && labels.Contains("ตั้งค่าคอลัมน์…") &&
            labels.Contains("ปุ่มรายละเอียดทุกช่องในคอลัมน์นี้") && labels.Contains("ความกว้างคอลัมน์…") &&
            labels.Contains("สร้างกลุ่มที่ยุบ/ขยายได้…") && labels.Any(s => s.StartsWith("ลบคอลัมน์", StringComparison.Ordinal)),
            string.Join(" | ", labels));
        var moveLeft = column.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ย้ายไปทางซ้าย");
        var moveRight = column.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ย้ายไปทางขวา");
        ok("first column cannot move left and can move right", moveLeft.IsEnabled == false && moveRight.IsEnabled, "");
        ok("move left shows Alt+Shift+Left",
            moveLeft.InputGesture is KeyGesture g && g.Key == Key.Left && g.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift),
            moveLeft.InputGesture?.ToString() ?? "");
        var make = column.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "สร้างกลุ่มที่ยุบ/ขยายได้…");
        ok("plain column can start a group", make.IsEnabled, "");
        column.Close();

        var row = Item(menu, "แถว");
        row.Open();
        Pump(window);
        var rowLabels = Headers(row.Items);
        ok("row menu lists moves, colour, detail, height",
            rowLabels.Contains("เลื่อนขึ้น") && rowLabels.Contains("สีแถว") && rowLabels.Contains("ไฮไลต์แถว") &&
            rowLabels.Any(s => s.Contains("ปุ่มรายละเอียด", StringComparison.Ordinal)) && rowLabels.Contains("ความสูงแถว…") &&
            rowLabels.Contains("เปิดตารางรายละเอียด") && rowLabels.Contains("ซ่อนปุ่มรายละเอียดของช่องนี้"),
            string.Join(" | ", rowLabels));
        var colour = row.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "สีแถว");
        var colourLabels = Headers(colour.Items);
        ok("row colour submenu has none and the eight colours",
            colourLabels.Contains("ไม่มีสี") && colourLabels.Contains("เหลือง") && colourLabels.Contains("เทา") && colourLabels.Count >= 9,
            string.Join(" | ", colourLabels));
        var up = row.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "เลื่อนขึ้น");
        var down = row.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "เลื่อนลง");
        ok("first row cannot move up and can move down", up.IsEnabled == false && down.IsEnabled, "");
        ok("move up shows Alt+Shift+Up",
            up.InputGesture is KeyGesture ug && ug.Key == Key.Up && ug.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift), "");
        Save(window, "menu-row-th");
        row.Close();

        // The EN/TH button left the toolbar: View → Language → English.
        var view = Item(menu, "มุมมอง");
        view.Open();
        Pump(window);
        var language = view.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ภาษา");
        language.Open();
        Pump(window);
        language.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "English")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        view.Close();
        Pump(window);
        var english = Headers(menu.Items);
        ok("View → Language updates the menu bar",
            english.SequenceEqual(["File", "Table", "Column", "Row", "View", "Help"]),
            string.Join(" | ", english));
        ok("View → Language updates the toolbar",
            window.FindControl<Button>("WallpaperButton")?.Content?.ToString() == "Wallpaper" &&
            window.FindControl<CheckBox>("HideDoneBox")?.Content?.ToString() == "Hide done rows",
            window.FindControl<Button>("WallpaperButton")?.Content?.ToString() ?? "");
        var file = Item(menu, "File");
        file.Open();
        Pump(window);
        var fileLabels = Headers(file.Items);
        ok("File menu keeps copy, image export, print and the wallpaper command",
            fileLabels.Contains("Copy table") && fileLabels.Contains("Copy with headers") && fileLabels.Contains("Export image") &&
            fileLabels.Contains("Print") && fileLabels.Contains("Set as wallpaper"),
            string.Join(" | ", fileLabels));
        file.Close();
        Pump(window);
        Save(window, "toolbar-menu-en");

        var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var doc = (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
        doc.RemoveColumn(doc.DueColumnIndex());
        doc.RemoveColumn(doc.StatusColumnIndex());
        typeof(MainWindow).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, ViewMode.Background);
        var tableEn = Item(menu, "Table");
        tableEn.Open();
        Pump(window);
        var viewSpots = tableEn.Items.OfType<MenuItem>().Where(i => i.Tag is string t && t.StartsWith("spot:", StringComparison.Ordinal)).ToList();
        ok("view mode disables both spots even when the columns are missing",
            viewSpots.Count == 2 && viewSpots.All(s => s.IsEnabled == false),
            string.Join(" | ", viewSpots.Select(s => $"{s.Tag} enabled={s.IsEnabled}")));
        tableEn.Close();
        Pump(window);
        typeof(MainWindow).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, ViewMode.Edit);
        tableEn.Open();
        Pump(window);
        var editSpots = tableEn.Items.OfType<MenuItem>().Where(i => i.Tag is string t && t.StartsWith("spot:", StringComparison.Ordinal)).ToList();
        ok("edit mode enables both spots when the columns are missing",
            editSpots.Count == 2 && editSpots.All(s => s.IsEnabled),
            string.Join(" | ", editSpots.Select(s => $"{s.Tag} enabled={s.IsEnabled}")));
        tableEn.Close();
    }

    /// <summary>Text runs drawn with another font than the bundled one (colour emoji excepted).</summary>
    static List<string> ForeignRuns(IEnumerable<Visual> roots)
    {
        var bad = new List<string>();
        foreach (var (family, text, owner) in Runs(roots))
            if (family != "Noto Sans Thai" && !EmojiOnly(text)) bad.Add($"{family}[{text}] in {owner}");
        return bad;
    }

    static int TextRunCount(IEnumerable<Visual> roots) => Runs(roots).Count();

    static IEnumerable<(string family, string text, string owner)> Runs(IEnumerable<Visual> roots)
    {
        foreach (var v in roots.SelectMany(r => r.GetVisualDescendants().Prepend(r)))
        {
            if (!v.IsEffectivelyVisible) continue;
            var layout = v switch
            {
                TextBlock tb when !string.IsNullOrEmpty(tb.Text) => tb.TextLayout,
                Avalonia.Controls.Presenters.TextPresenter tp when !string.IsNullOrEmpty(tp.Text) => tp.TextLayout,
                _ => null
            };
            if (layout == null) continue;
            foreach (var line in layout.TextLines)
                foreach (var run in line.TextRuns)
                    if (run is Avalonia.Media.TextFormatting.ShapedTextRun sr)
                        yield return (sr.GlyphRun.GlyphTypeface.FamilyName, new string(sr.Text.Span), v.GetType().Name);
        }
    }

    static bool EmojiOnly(string text) =>
        text.EnumerateRunes().All(r => Rune.IsWhiteSpace(r) || r.Value >= 0x1F000 || r.Value is 0x23F0 or 0xFE0F or 0x200D);

    static MenuItem Item(Menu menu, string header) =>
        menu.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == header);

    static List<string> Headers(ItemCollection items) =>
        items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "").ToList();

    static bool Fits(Control host, Control? control)
    {
        if (control == null || control.Bounds.Width < 1 || control.Bounds.Height < 1) return false;
        var p = control.TranslatePoint(new Point(control.Bounds.Width - 1, control.Bounds.Height / 2), host);
        return p is { } point && point.X >= 0 && point.X <= host.Bounds.Width + 1 && point.Y >= 0 && point.Y <= host.Bounds.Height + 1;
    }

    static void Click(Window window, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException(control.Name + " has no point");
        window.MouseMove(p);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
    }

    static void Save(Window window, string name)
    {
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            var frame = window.CaptureRenderedFrame() ?? window.GetLastRenderedFrame();
            if (frame == null)
            {
                Console.WriteLine("screenshot skipped: no frame");
                return;
            }
            var artifacts = "/opt/cursor/artifacts";
            Directory.CreateDirectory(artifacts);
            var path = Path.Combine(artifacts, name + ".png");
            frame.Save(path);
            Console.WriteLine($"screenshot {path} {frame.PixelSize.Width}x{frame.PixelSize.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("screenshot skipped: " + ex.Message);
        }
    }
}
