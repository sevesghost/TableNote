using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TableNote;

AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-detail-popup.log");
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Localizer.SetLanguage("en");

int pass = 0, fail = 0;
void Ok(bool c, string name, string detail = "")
{
    if (c) pass++; else fail++;
    Console.WriteLine($"{(c ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
}
void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
}
Button? Find(Window w, string text) =>
    w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text);
void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

var seed = CellDetailOps.CreateDefault();
var dlg = new CellDetailDialog(Localizer.T("detail.title", "Detail", 1), seed, readOnly: false);
dlg.Show();
Pump(50);
var boxes = dlg.GetLogicalDescendants().OfType<TextBox>().ToList();
Ok(boxes.Count == seed.Columns.Count + seed.Columns.Count * seed.Rows.Count, "dialog builds a header and a box per cell", boxes.Count.ToString());
Ok(boxes.Count > 2 && boxes[0].Text == "Item" && boxes[1].Text == "Value", "default headers are Item and Value", string.Join(",", boxes.Take(2).Select(b => b.Text)));
boxes[2].Text = "Nails";
var add = Find(dlg, Localizer.T("detail.add_row"));
Ok(add != null, "add-row button is present");
if (add != null) Click(add);
Pump(30);
Ok(dlg.GetLogicalDescendants().OfType<TextBox>().Count() == boxes.Count + seed.Columns.Count, "add row adds one box per column");
Click(Find(dlg, Localizer.T("detail.close"))!);
Pump(30);
Ok(dlg.Saved && dlg.Result != null, "closing the popup saves");
Ok(dlg.Result!.Rows.Count == seed.Rows.Count + 1, "saved table has the new row", dlg.Result?.Rows.Count.ToString() ?? "");
Ok(dlg.Result!.Rows[0][0] == "Nails" && dlg.Result.Columns[0] == "Item", "typed value and header are kept", dlg.Result.Rows[0][0]);

var cancelDlg = new CellDetailDialog(Localizer.T("detail.title", "Detail", 2), CellDetailOps.CreateDefault(), false);
cancelDlg.Show();
Pump(20);
cancelDlg.GetLogicalDescendants().OfType<TextBox>().ElementAt(2).Text = "Discard me";
Click(Find(cancelDlg, Localizer.T("common.cancel"))!);
Pump(20);
Ok(!cancelDlg.Saved && cancelDlg.Result == null, "cancel discards the detail edit");

var chart = new CellDetail { Kind = "chart" };
chart.Extra["kind"] = System.Text.Json.JsonDocument.Parse("\"chart\"").RootElement.Clone();
chart.Extra["axis"] = System.Text.Json.JsonDocument.Parse("\"y\"").RootElement.Clone();
var unknown = new CellDetailDialog("chart", chart, false);
unknown.Show();
Pump(20);
Click(Find(unknown, Localizer.T("detail.close"))!);
Pump(20);
Ok(!unknown.Saved, "an unknown detail kind is not overwritten on close");

var readOnly = new CellDetailDialog(Localizer.T("detail.title", "Detail", 3), CellDetailOps.CreateDefault(), true);
readOnly.Show();
Pump(20);
var editor = readOnly.GetLogicalDescendants().OfType<TextBox>().First();
Ok(editor.IsReadOnly, "background mode opens the detail table read-only");
Click(Find(readOnly, Localizer.T("detail.close"))!);
Pump(20);
Ok(!readOnly.Saved, "closing a read-only detail does not write");

Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
