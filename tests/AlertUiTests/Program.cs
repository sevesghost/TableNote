using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// Drives the real main window (headless): add, edit, move up with the button, drag to reorder, delete, reload.
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}

var dir = Directory.CreateTempSubdirectory("tn-alert-ui").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");

try
{
    AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    var window = new MainWindow();
    window.Width = 900;
    window.Height = 700;
    window.Show();
    Pump(window);

    // 2.6.0: no alert panel while there are no boxes; the first one comes from Table → Add alert box.
    var host = window.FindControl<Border>("AlertHost") ?? throw new Exception("alert panel missing");
    Ok(!host.IsVisible, "no alert panel while there are no alert boxes");
    var tableMenu = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "Table");
    tableMenu.Open();
    Pump(window);
    var addItem = tableMenu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header?.ToString() == "Add alert box");
    Ok(addItem is { IsEnabled: true }, "Table menu has an enabled Add alert box item");
    addItem?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    tableMenu.Close();
    Pump(window);
    Ok(host.IsVisible && Cards(window).Count == 1, "the menu item adds the first box and shows the panel", Cards(window).Count.ToString());
    var add = window.FindControl<Button>("AddAlertButton") ?? throw new Exception("Add button missing");
    Ok(add.IsVisible, "add button is visible in the panel once there is a box");

    TypeInto(window, TitleBox(Card(window, 0)), "Alpha");
    TypeInto(window, BodyBox(Card(window, 0)), "one");
    Click(window, add);
    Pump(window);
    Ok(Cards(window).Count == 2, "two alert boxes on screen", Cards(window).Count.ToString());
    TypeInto(window, TitleBox(Card(window, 1)), "Beta");
    TypeInto(window, BodyBox(Card(window, 1)), "two");
    Pump(window);
    Ok(Titles(window).SequenceEqual(["Alpha", "Beta"]), "typed titles stay in order", string.Join(" | ", Titles(window)));
    SaveFrame(window, "alert-boxes-added");

    var before = Ids(window);
    var upButtons = Cards(window).Select(UpButton).ToList();
    Ok(!upButtons[0].IsEnabled && upButtons[1].IsEnabled, "Up is disabled on the first box and enabled on the second");
    upButtons[1].Focus();
    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, "");
    Pump(window);
    if (!Titles(window).SequenceEqual(["Beta", "Alpha"]))
    {
        // Space did not activate the button; click it. Still the accessible control, not a drag.
        Click(window, UpButton(Card(window, 1)));
        Pump(window);
    }
    Ok(Titles(window).SequenceEqual(["Beta", "Alpha"]), "move up without dragging", string.Join(" | ", Titles(window)));
    Ok(Ids(window).SequenceEqual([before[1], before[0]]), "move up keeps the same ids", string.Join(" | ", Ids(window)));

    DragCard(window, fromIndex: 1, toIndex: 0);
    Pump(window);
    Ok(Titles(window).SequenceEqual(["Alpha", "Beta"]), "drag the lower box above the first", string.Join(" | ", Titles(window)));
    SaveFrame(window, "alert-boxes-reordered");

    var keepId = Ids(window)[1];
    Click(window, DeleteButton(Card(window, 0)));
    Pump(window);
    var dialog = ConfirmDialog(window);
    Ok(dialog != null && Cards(window).Count == 2, "delete asks before removing the box");
    if (dialog == null) throw new Exception("confirm dialog did not open");
    Click(dialog, DialogButton(dialog, "Cancel"));
    Pump(window);
    Ok(Cards(window).Count == 2 && Titles(window).SequenceEqual(["Alpha", "Beta"]), "cancel keeps the alert box", string.Join(" | ", Titles(window)));
    Click(window, DeleteButton(Card(window, 0)));
    Pump(window);
    dialog = ConfirmDialog(window) ?? throw new Exception("confirm dialog did not open the second time");
    Click(dialog, DialogButton(dialog, "Delete"));
    Pump(window);
    Ok(Cards(window).Count == 1 && Titles(window).SequenceEqual(["Beta"]), "confirm delete removes that box", string.Join(" | ", Titles(window)));
    Ok(Ids(window).SequenceEqual([keepId]), "the remaining box keeps its id");

    window.Close();
    Pump(window);
    var loaded = NoteStore.Load(note).Document;
    Ok(loaded.AlertBoxes.Count == 1 && loaded.AlertBoxes[0].Id == keepId
        && loaded.AlertBoxes[0].Title == "Beta" && loaded.AlertBoxes[0].Text == "two",
        "reload from note.json keeps the remaining box",
        loaded.AlertBoxes.Count == 0 ? "none" : string.Join(" | ", loaded.AlertBoxes.Select(b => b.Id + ":" + b.Title + "/" + b.Text)));
    Ok(loaded.Rows.Count > 0 && loaded.Columns.Count >= 4, "the table is still there after alert edits");
}
catch (Exception ex)
{
    Console.WriteLine("FAIL harness — " + ex);
    fail++;
}

Console.WriteLine(fail == 0 ? "AlertUiTests: all passed" : $"AlertUiTests: {fail} failed");
return fail == 0 ? 0 : 1;

static void Pump(Window window)
{
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
}

static List<Border> Cards(Window window) =>
    window.GetVisualDescendants().OfType<Border>()
        .Where(b => (AutomationProperties.GetAutomationId(b) ?? "").StartsWith("alert-", StringComparison.Ordinal))
        .ToList();

static Border Card(Window window, int index) => Cards(window)[index];

static TextBox TitleBox(Border card) => card.GetVisualDescendants().OfType<TextBox>().First(t => t.FontWeight == Avalonia.Media.FontWeight.SemiBold);
static TextBox BodyBox(Border card) => card.GetVisualDescendants().OfType<TextBox>().First(t => t.FontWeight != Avalonia.Media.FontWeight.SemiBold);

static Button UpButton(Border card) => ButtonWith(card, "Up");
static Button DeleteButton(Border card) => ButtonWith(card, "Delete");

static Window? ConfirmDialog(Window owner) =>
    owner.OwnedWindows.FirstOrDefault(w => w.IsVisible);

static Button DialogButton(Window dialog, string content) =>
    dialog.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content);

static Button ButtonWith(Border card, string content) =>
    card.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content);

static List<string> Titles(Window window) => Cards(window).Select(c => TitleBox(c).Text ?? "").ToList();

static List<string> Ids(Window window) =>
    Cards(window).Select(c => (AutomationProperties.GetAutomationId(c) ?? "")["alert-".Length..]).ToList();

static Point Center(Visual window, Visual control)
{
    var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
    if (p == null || control.Bounds.Width < 1 || control.Bounds.Height < 1)
        throw new InvalidOperationException($"control has no layout ({control.GetType().Name} bounds {control.Bounds})");
    return p.Value;
}

static void Click(Window window, Control control)
{
    var p = Center(window, control);
    window.MouseMove(p);
    window.MouseDown(p, MouseButton.Left);
    window.MouseUp(p, MouseButton.Left);
}

static void TypeInto(Window window, TextBox box, string text)
{
    Click(window, box);
    Pump(window);
    window.KeyTextInput(text);
    Pump(window);
}

static void DragCard(Window window, int fromIndex, int toIndex)
{
    var from = Card(window, fromIndex);
    var grip = from.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "⋮⋮");
    var start = Center(window, grip);
    var target = Card(window, toIndex);
    var top = target.TranslatePoint(new Point(target.Bounds.Width / 2, 4), window)
        ?? throw new InvalidOperationException("no drop point");
    window.MouseMove(start);
    window.MouseDown(start, MouseButton.Left);
    window.MouseMove(new Point(start.X, start.Y - 24));
    window.MouseMove(top);
    window.MouseUp(top, MouseButton.Left);
}

static void SaveFrame(Window window, string name)
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
        if (!Directory.Exists(artifacts)) return;
        var path = Path.Combine(artifacts, name + ".png");
        frame.Save(path);
        Console.WriteLine($"screenshot {path} {frame.PixelSize.Width}x{frame.PixelSize.Height}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("screenshot skipped: " + ex.Message);
    }
}
