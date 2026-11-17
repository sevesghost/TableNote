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
            .SetupWithoutStarting();

        var window = new MainWindow { Width = 980, Height = 640 };
        window.Show();
        Pump(window);

        var menu = window.FindControl<Menu>("MainMenu") ?? throw new Exception("menu bar missing");
        var titles = Headers(menu.Items);
        ok("thai menu titles",
            titles.SequenceEqual(["ไฟล์", "ตาราง", "คอลัมน์", "แถว", "มุมมอง", "ช่วยเหลือ"]),
            string.Join(" | ", titles));
        ok("toolbar group แก้ตาราง", window.FindControl<TextBlock>("GroupTableLabel")?.Text == "แก้ตาราง",
            window.FindControl<TextBlock>("GroupTableLabel")?.Text ?? "");
        ok("toolbar group คัดลอก", window.FindControl<TextBlock>("GroupCopyLabel")?.Text == "คัดลอก / ส่งออก",
            window.FindControl<TextBlock>("GroupCopyLabel")?.Text ?? "");
        ok("toolbar group มุมมอง", window.FindControl<TextBlock>("GroupViewLabel")?.Text == "มุมมอง", "");
        ok("toolbar group ระบบ", window.FindControl<TextBlock>("GroupSystemLabel")?.Text == "ตั้งค่าระบบ", "");
        ok("toolbar group ช่วยเหลือ", window.FindControl<TextBlock>("GroupHelpLabel")?.Text == "ช่วยเหลือ", "");

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
        table.Close();
        Pump(window);

        Save(window, "toolbar-menu-th-wide");

        window.Width = 480;
        Pump(window);
        var scroll = window.FindControl<ScrollViewer>("ToolScroll") ?? throw new Exception("toolbar scroll missing");
        ok("toolbar stays within its band", scroll.Bounds.Height <= 221, scroll.Bounds.Height.ToString("0"));
        ok("narrow window keeps add-row inside the toolbar", Fits(scroll, window.FindControl<Button>("AddRowButton")), "");
        ok("narrow window keeps print inside the toolbar", Fits(scroll, window.FindControl<Button>("PrintButton")), "");
        Save(window, "toolbar-menu-th-narrow");
        scroll.Offset = scroll.Offset.WithY(scroll.Extent.Height);
        Pump(window);
        ok("narrow window scrolls help into view", Fits(scroll, window.FindControl<Button>("HelpButton")), "");
        ok("narrow window scrolls pin into view", Fits(scroll, window.FindControl<Button>("BackgroundButton")), "");
        Save(window, "toolbar-menu-th-narrow-scrolled");
        scroll.Offset = scroll.Offset.WithY(0);
        Pump(window);

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

        Click(window, window.FindControl<Button>("LanguageButton")!);
        Pump(window);
        var english = Headers(menu.Items);
        ok("EN/TH updates the menu bar",
            english.SequenceEqual(["File", "Table", "Column", "Row", "View", "Help"]),
            string.Join(" | ", english));
        ok("EN/TH updates the toolbar groups",
            window.FindControl<TextBlock>("GroupTableLabel")?.Text == "Edit table" &&
            window.FindControl<TextBlock>("GroupSystemLabel")?.Text == "System",
            window.FindControl<TextBlock>("GroupTableLabel")?.Text ?? "");
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
