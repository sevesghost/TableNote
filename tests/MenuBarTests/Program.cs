using System.Text.RegularExpressions;
using TableNote;

// Menu bar coverage, shared enable rules, and the context-menu baseline source check.
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-menubar-tests.log");
var fail = 0;
void Ok(string name, bool ok, string detail = "")
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " — " + detail));
    if (!ok) fail++;
}

string Root()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "TableNote.csproj"))) d = d.Parent;
    return d?.FullName ?? throw new Exception("TableNote.csproj not found");
}

var root = Root();
string Read(string rel) => File.ReadAllText(Path.Combine(root, rel));
var columnMenu = Read("MainWindow.ColumnMenu.cs");
var rowMenu = Read("MainWindow.RowMenu.cs");
var settings = Read("MainWindow.Settings.cs");
var detail = Read("MainWindow.DetailPopup.cs");
var sizing = Read("MainWindow.Sizing.cs");
var main = Read("MainWindow.axaml.cs");
var menuBar = Read("MainWindow.MenuBar.cs");
var axaml = Read("MainWindow.axaml");

// Baseline ids the top menu must cover. Tray-only ids are intentionally absent.
var columnIds = new[]
{
    "col.cut", "col.copy", "col.paste",
    "col.move_left", "col.move_right", "col.move_first", "col.move_last",
    "colset.menu", "detail.column_toggle", "detail.cell_toggle", "size.col_menu",
    "colgroup.make", "colgroup.collapse", "colgroup.expand", "colgroup.add_child",
    "colgroup.rename_children", "colgroup.remove_child", "colgroup.remove_named",
    "colgroup.ungroup", "colgroup.child_of", "col.delete"
};
var rowIds = new[]
{
    "row.move_up", "row.move_down", "row.move_top", "row.move_bottom",
    "rowstyle.colour", "rowstyle.none",
    "colour.yellow", "colour.orange", "colour.red", "colour.pink",
    "colour.purple", "colour.blue", "colour.green", "colour.gray",
    "rowstyle.highlight", "rowstyle.clear", "detail.row_toggle", "size.row_menu"
};
var detailIds = new[] { "detail.open", "detail.hide_cell" };
var covered = columnIds.Concat(rowIds).Concat(detailIds).ToHashSet(StringComparer.Ordinal);
var coverageDiff = covered.Except(MenuBarCoverage.All()).Concat(MenuBarCoverage.All().Except(covered)).ToList();
Ok("coverage list matches the column/row/detail baseline", coverageDiff.Count == 0, string.Join(", ", coverageDiff));

// The line above mutates `covered` if it fails. Rebuild for the rest.
covered = columnIds.Concat(rowIds).Concat(detailIds).ToHashSet(StringComparer.Ordinal);
Ok("no duplicate coverage ids", covered.Count == columnIds.Length + rowIds.Length + detailIds.Length);

bool HasT(string source, string id) => source.Contains($"Localizer.T(\"{id}\"", StringComparison.Ordinal);
void InFile(string fileName, string source, params string[] ids)
{
    var missing = ids.Where(id => !HasT(source, id)).ToList();
    Ok(fileName + " still has " + string.Join(", ", ids.Take(3)) + (ids.Length > 3 ? "…" : ""),
        missing.Count == 0, string.Join(", ", missing));
}

InFile("ColumnMenu", columnMenu, "col.cut", "col.copy", "col.paste", "col.move_left", "col.move_right",
    "col.move_first", "col.move_last", "col.delete",
    "colgroup.make", "colgroup.collapse", "colgroup.expand", "colgroup.add_child",
    "colgroup.rename_children", "colgroup.remove_child", "colgroup.remove_named",
    "colgroup.ungroup", "colgroup.child_of");
InFile("Settings", settings, "colset.menu", "rowstyle.colour", "rowstyle.none", "rowstyle.highlight", "rowstyle.clear",
    "colour.yellow", "colour.orange", "colour.red", "colour.pink", "colour.purple", "colour.blue", "colour.green", "colour.gray");
InFile("DetailPopup", detail, "detail.column_toggle", "detail.cell_toggle", "detail.row_toggle", "detail.open", "detail.hide_cell");
InFile("Sizing", sizing, "size.col_menu", "size.row_menu");
InFile("RowMenu", rowMenu, "row.move_up", "row.move_down", "row.move_top", "row.move_bottom");
InFile("MainWindow tray", main, "tray.edit", "tray.help", "tray.log", "tray.export", "tray.language",
    "tray.zoom_in", "tray.zoom_out", "tray.zoom_reset", "tray.center", "tray.quit");

Ok("column menu still calls RequestMoveColumn", columnMenu.Contains("RequestMoveColumn(", StringComparison.Ordinal));
Ok("column menu still calls RequestDeleteColumn", columnMenu.Contains("RequestDeleteColumn(", StringComparison.Ordinal));
Ok("column menu still calls RequestMakeGroup", columnMenu.Contains("RequestMakeGroup(", StringComparison.Ordinal));
Ok("column menu still calls RequestToggleGroup", columnMenu.Contains("RequestToggleGroup(", StringComparison.Ordinal));
Ok("column menu still calls RequestAddChild", columnMenu.Contains("RequestAddChild(", StringComparison.Ordinal));
Ok("column menu still calls RequestRenameChildren", columnMenu.Contains("RequestRenameChildren(", StringComparison.Ordinal));
Ok("column menu still calls RequestUngroup", columnMenu.Contains("RequestUngroup(", StringComparison.Ordinal));
Ok("header cut/copy/paste still call Cut Copy Paste",
    columnMenu.Contains("box.Cut()", StringComparison.Ordinal) &&
    columnMenu.Contains("box.Copy()", StringComparison.Ordinal) &&
    columnMenu.Contains("box.Paste()", StringComparison.Ordinal));
Ok("settings still open via OpenColumnSettings and ChangeRowStyle",
    settings.Contains("OpenColumnSettings(", StringComparison.Ordinal) && settings.Contains("ChangeRowStyle(", StringComparison.Ordinal));
Ok("detail still toggles and opens",
    detail.Contains("ToggleColumnButton(", StringComparison.Ordinal) &&
    detail.Contains("ToggleCellButton(", StringComparison.Ordinal) &&
    detail.Contains("OpenDetailPopup(", StringComparison.Ordinal));
Ok("size still opens the dialogs",
    sizing.Contains("OpenColumnWidth(", StringComparison.Ordinal) && sizing.Contains("OpenRowHeight(", StringComparison.Ordinal));
Ok("row moves still call MoveRowBy", rowMenu.Contains("MoveRowBy(", StringComparison.Ordinal));

Ok("menu bar builds the column menu from BuildColumnMenuItems", menuBar.Contains("BuildColumnMenuItems(", StringComparison.Ordinal));
Ok("menu bar builds the row menu from CreateRowMenuItems", menuBar.Contains("CreateRowMenuItems(", StringComparison.Ordinal));
Ok("menu bar builds detail open/hide from BuildDetailButtonMenuItems", menuBar.Contains("BuildDetailButtonMenuItems(", StringComparison.Ordinal));
Ok("column gestures stay on the shared items",
    columnMenu.Contains("Key.X, KeyModifiers.Control", StringComparison.Ordinal) &&
    columnMenu.Contains("Key.Left, KeyModifiers.Alt | KeyModifiers.Shift", StringComparison.Ordinal) &&
    rowMenu.Contains("Key.Up, KeyModifiers.Alt | KeyModifiers.Shift", StringComparison.Ordinal));

var init = main.IndexOf("InitSettings(", StringComparison.Ordinal);
var popup = main.IndexOf("InitDetailPopup(", StringComparison.Ordinal);
var sizingInit = main.IndexOf("InitSizing(", StringComparison.Ordinal);
Ok("constructor still registers settings, then detail, then sizing", init >= 0 && init < popup && popup < sizingInit);
Ok("detail and sizing still wrap the previous menu delegates",
    detail.Contains("prevCol?.Invoke", StringComparison.Ordinal) && detail.Contains("prevRow?.Invoke", StringComparison.Ordinal) &&
    sizing.Contains("prevCol?.Invoke", StringComparison.Ordinal) && sizing.Contains("prevRow?.Invoke", StringComparison.Ordinal));
var extra = columnMenu.IndexOf("_extraColumnMenuItems", StringComparison.Ordinal);
var group = columnMenu.IndexOf("BuildColumnGroupMenuItems(", StringComparison.Ordinal);
Ok("group block is still after the extra column items", extra >= 0 && group > extra);

Ok("menu bar binds the alert-date and status spots through MenuCommandRules",
    menuBar.Contains("Localizer.T(\"menu.spot.add_due\")", StringComparison.Ordinal) &&
    menuBar.Contains("Localizer.T(\"menu.spot.add_status\")", StringComparison.Ordinal) &&
    menuBar.Contains("spot:add-alert-date", StringComparison.Ordinal) &&
    menuBar.Contains("spot:add-status", StringComparison.Ordinal) &&
    menuBar.Contains("MenuCommandRules.AddDueColumn(", StringComparison.Ordinal) &&
    menuBar.Contains("MenuCommandRules.AddStatusColumn(", StringComparison.Ordinal) &&
    menuBar.Contains("AddSystemColumnAsync(ColumnRoles.Due)", StringComparison.Ordinal) &&
    menuBar.Contains("AddSystemColumnAsync(ColumnRoles.Status)", StringComparison.Ordinal));
// 2.6.0: one toolbar row of everyday commands (no labelled groups); the rest is in the menu bar.
Ok("one toolbar row in the window layout",
    axaml.Contains("<WrapPanel x:Name=\"EditTools\">", StringComparison.Ordinal) &&
    axaml.Contains("x:Name=\"WallpaperButton\"", StringComparison.Ordinal) &&
    axaml.Contains("x:Name=\"MainMenu\"", StringComparison.Ordinal) &&
    !axaml.Contains("GroupTableLabel", StringComparison.Ordinal) &&
    !axaml.Contains("toolgroup", StringComparison.Ordinal));
Ok("commands that left the toolbar are still in the menu bar",
    menuBar.Contains("\"toolbar.copy_all\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"toolbar.copy_headers\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"toolbar.export_image\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"toolbar.print\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"toolbar.delete_column\"", StringComparison.Ordinal) &&
    menuBar.Contains("CheckItem(\"toolbar.topmost\"", StringComparison.Ordinal) &&
    menuBar.Contains("CheckItem(\"toolbar.lock\"", StringComparison.Ordinal) &&
    menuBar.Contains("CheckItem(\"toolbar.launch_at_login\"", StringComparison.Ordinal) &&
    menuBar.Contains("CheckItem(\"toolbar.mute\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"menu.wallpaper\"", StringComparison.Ordinal) &&
    menuBar.Contains("\"menu.add_alert\"", StringComparison.Ordinal) &&
    menuBar.Contains("FillLanguageMenu", StringComparison.Ordinal) &&
    menuBar.Contains("\"tray.help\"", StringComparison.Ordinal) && menuBar.Contains("\"tray.log\"", StringComparison.Ordinal));
Ok("file format version is untouched", Read("Core/NoteDocument.cs").Contains("public const int CurrentVersion = 6;", StringComparison.Ordinal));

var menuKeys = new[]
{
    "menu.file", "menu.table", "menu.column", "menu.row", "menu.view", "menu.help",
    "menu.system", "menu.language",
    "menu.wallpaper", "menu.add_alert",
    "menu.spot.add_due", "menu.spot.add_status", "menu.spot.pending"
};
Dictionary<string, string> Lang(string code) =>
    Localizer.Parse(File.ReadAllText(Path.Combine(root, "lang", code + ".json")));
var en = Lang("en");
var th = Lang("th");
var thai = new Regex("[\u0E00-\u0E7F]");
foreach (var key in menuKeys)
{
    en.TryGetValue(key, out var ev);
    th.TryGetValue(key, out var tv);
    Ok("en has " + key, ev != null && ev.Trim().Length > 0 && !thai.IsMatch(ev), ev ?? "");
    Ok("th has " + key, tv != null && thai.IsMatch(tv), tv ?? "");
}
Ok("Thai spot labels are the agreed wording",
    th["menu.spot.add_due"] == "เพิ่มคอลัมน์วันที่แจ้งเตือน" && th["menu.spot.add_status"] == "เพิ่มคอลัมน์สถานะ");
Ok("language files agree on menu keys", menuKeys.All(k => en.ContainsKey(k) && th.ContainsKey(k)));

// Enable / disable rules. Default note: title, detail, status, due.
var doc = NoteDocument.CreateDefault();
var status = doc.StatusColumnIndex();
var due = doc.DueColumnIndex();
var count = doc.Columns.Count;
var room = NoteDocument.MaxUserColumns - count;
Ok("sample has status and due", status == 2 && due == 3 && count == 4, $"status {status} due {due} count {count}");
Ok("alert-date and status spots are disabled while those columns exist",
    !MenuCommandRules.AddDueColumn(doc) && !MenuCommandRules.AddStatusColumn(doc));
doc.RemoveColumn(doc.DueColumnIndex());
Ok("alert-date spot enables after the reminder column is removed",
    MenuCommandRules.AddDueColumn(doc) && !MenuCommandRules.AddStatusColumn(doc));
doc.RemoveColumn(doc.StatusColumnIndex());
Ok("status spot enables after the status column is removed",
    MenuCommandRules.AddStatusColumn(doc) && MenuCommandRules.AddDueColumn(doc));
Ok("re-adding status disables only that spot",
    SystemColumnToggle.Add(doc, ColumnRoles.Status) == null &&
    !MenuCommandRules.AddStatusColumn(doc) && MenuCommandRules.AddDueColumn(doc));
Ok("re-adding the reminder column disables that spot again",
    SystemColumnToggle.Add(doc, ColumnRoles.Due) == null &&
    !MenuCommandRules.AddDueColumn(doc) && !MenuCommandRules.AddStatusColumn(doc));

Ok("no column disables move and delete",
    !MenuCommandRules.MoveLeft(true, -1, count) &&
    !MenuCommandRules.MoveRight(true, -1, count) &&
    !MenuCommandRules.DeleteColumn(true, -1, count, false) &&
    !MenuCommandRules.ColumnSettings(true, -1, count) &&
    !MenuCommandRules.MakeGroup(true, -1, count, false, room) &&
    !MenuCommandRules.ColumnClipboard(true, true, -1, count));
Ok("clipboard needs a header box", !MenuCommandRules.ColumnClipboard(true, false, 1, count));
Ok("clipboard off outside edit", !MenuCommandRules.ColumnClipboard(false, true, 1, count));
Ok("first column cannot move left or first",
    !MenuCommandRules.MoveLeft(true, 0, count) && !MenuCommandRules.MoveFirst(true, 0, count) &&
    MenuCommandRules.MoveRight(true, 0, count) && MenuCommandRules.MoveLast(true, 0, count));
Ok("last column cannot move right or last",
    !MenuCommandRules.MoveRight(true, count - 1, count) && !MenuCommandRules.MoveLast(true, count - 1, count) &&
    MenuCommandRules.MoveLeft(true, count - 1, count));
Ok("middle column can move both ways",
    MenuCommandRules.MoveLeft(true, 1, count) && MenuCommandRules.MoveRight(true, 1, count));
Ok("moves are off outside edit", !MenuCommandRules.MoveLeft(false, 1, count) && !MenuCommandRules.MoveRight(false, 1, count));
Ok("due column cannot be deleted", !MenuCommandRules.DeleteColumn(true, due, count, true));
Ok("a normal column can be deleted when more than one remains", MenuCommandRules.DeleteColumn(true, 0, count, false));
Ok("the only column cannot be deleted", !MenuCommandRules.DeleteColumn(true, 0, 1, false));
Ok("status and due cannot become a group",
    !MenuCommandRules.MakeGroup(true, status, count, true, room) &&
    !MenuCommandRules.MakeGroup(true, due, count, true, room));
Ok("a plain column can become a group when there is room", MenuCommandRules.MakeGroup(true, 0, count, false, room));
Ok("no room blocks a new group and a new child",
    !MenuCommandRules.MakeGroup(true, 0, count, false, 0) &&
    !MenuCommandRules.AddChild(true, 0, 1));
Ok("twelve children block another child", !MenuCommandRules.AddChild(true, 5, ColumnGroupOps.MaxChildCount));
Ok("a collapsed column cannot set width", !MenuCommandRules.ColumnWidth(true, 1, count, true));
Ok("a visible column can set width in edit mode", MenuCommandRules.ColumnWidth(true, 1, count, false));
Ok("child-of is never enabled", !MenuCommandRules.ChildOfLabel());
Ok("removing the only column is disabled", !MenuCommandRules.RemoveThisColumn(true, 1));
Ok("detail cell toggle needs a row", !MenuCommandRules.DetailCellToggle(true, 1, count, 0) && MenuCommandRules.DetailCellToggle(true, 1, count, 2));

var visible = Enumerable.Range(0, doc.Rows.Count).ToList();
Ok("first row cannot move up or to the top",
    !MenuCommandRules.RowMove(true, false, visible, 0, RowMoveKind.Up) &&
    !MenuCommandRules.RowMove(true, false, visible, 0, RowMoveKind.Top) &&
    MenuCommandRules.RowMove(true, false, visible, 0, RowMoveKind.Down));
Ok("last row cannot move down or to the bottom",
    !MenuCommandRules.RowMove(true, false, visible, visible.Count - 1, RowMoveKind.Down) &&
    !MenuCommandRules.RowMove(true, false, visible, visible.Count - 1, RowMoveKind.Bottom) &&
    MenuCommandRules.RowMove(true, false, visible, visible.Count - 1, RowMoveKind.Up));
Ok("a busy edit cannot move a row", !MenuCommandRules.RowMove(true, true, visible, 1, RowMoveKind.Up));
Ok("row commands need edit mode and a row",
    !MenuCommandRules.RowMove(false, false, visible, 1, RowMoveKind.Up) &&
    !MenuCommandRules.RowStyleMenu(true, false) &&
    !MenuCommandRules.RowHeight(true, false) &&
    !MenuCommandRules.DetailRowToggle(true, false, true) &&
    MenuCommandRules.RowHeight(true, true) &&
    MenuCommandRules.RowClear(true, true, true) &&
    !MenuCommandRules.RowClear(true, true, false));
Ok("hidden done rows are not a move target",
    !MenuCommandRules.RowMove(true, false, visible.Where(i => i != 1).ToList(), 1, RowMoveKind.Up));
Ok("detail open follows the button, hide also needs edit mode",
    MenuCommandRules.DetailOpen(true) && !MenuCommandRules.DetailOpen(false) &&
    MenuCommandRules.DetailHide(true, true) && !MenuCommandRules.DetailHide(false, true) &&
    !MenuCommandRules.DetailHide(true, false));

try { MenuBarHeadless.Run(Ok); }
catch (Exception ex) { Ok("headless menu bar", false, ex.ToString()); }

Console.WriteLine(fail == 0 ? "MenuBarTests: all passed" : $"MenuBarTests: {fail} failed");
return fail == 0 ? 0 : 1;
