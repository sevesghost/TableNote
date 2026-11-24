// 2.7.0 calendar. Run: dotnet run -c Release --project tests/CalendarTests (headless, no display needed).
// Core: the month grid, which rows fall on which day, later rounds of repeating tasks, adding / moving rows,
// texts (Thai with the Buddhist year), settings.json. UI: the real editor with the calendar window and the day
// pop-up table, driven with the mouse and keys: every change made there is in the table, and the other way round.
// Wallpaper: the calendar card at the bottom right of the screen (with the table, or alone), on a fake desktop.
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using TableNote;

var root = Directory.CreateTempSubdirectory("tn-calendar-").FullName;
Environment.SetEnvironmentVariable("TABLENOTE_DATA", Path.Combine(root, "note.json"));
AppLog.PathOverride = Path.Combine(root, "tablenote.log");
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();
Localizer.SetLanguage("en");

int pass = 0, fail = 0;
void Ok(bool ok, string name, string detail = "")
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " — " + detail));
    if (ok) pass++; else fail++;
}

try { Core(Ok); } catch (Exception ex) { Ok(false, "core harness", ex.ToString()); }
try { Texts(Ok); } catch (Exception ex) { Ok(false, "texts harness", ex.ToString()); }
try { Settings(Ok, root); } catch (Exception ex) { Ok(false, "settings harness", ex.ToString()); }
try { Card(Ok, root); } catch (Exception ex) { Ok(false, "card harness", ex.ToString()); }
try { Ui(Ok, root); } catch (Exception ex) { Ok(false, "ui harness", ex.ToString()); }
try { Wall(Ok, root); } catch (Exception ex) { Ok(false, "wallpaper harness", ex.ToString()); }
try { Old(Ok, root); } catch (Exception ex) { Ok(false, "old rows harness", ex.ToString()); }

Console.WriteLine($"\nCalendarTests: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

static NoteDocument Doc(int rows = 6)
{
    var doc = NoteDocument.CreateDefault();
    while (doc.Rows.Count < rows) doc.AppendRow();
    return doc;
}

static void Row(NoteDocument doc, int row, string title, string due, string status = "", string detail = "")
{
    doc.SetCellRaw(row, doc.IndexOfRole(ColumnRoles.Title), title);
    doc.SetCellRaw(row, doc.IndexOfRole(ColumnRoles.Detail), detail);
    doc.SetCellRaw(row, doc.DueColumnIndex(), due);
    doc.SetCellRaw(row, doc.StatusColumnIndex(), status);
}

static void Core(Check ok)
{
    // ── the month grid ──
    var nov = CalendarOps.MonthGrid(2026, 11, 0);
    ok(nov.Count == 42 && nov[0] == new DateTime(2026, 11, 1) && nov[0].DayOfWeek == DayOfWeek.Sunday && nov[^1] == new DateTime(2026, 12, 12),
        "November 2026, weeks from Sunday: 1 Nov (a Sunday) to 12 Dec, six weeks", $"{nov[0]:d} … {nov[^1]:d}");
    var novMon = CalendarOps.MonthGrid(2026, 11, 1);
    ok(novMon[0] == new DateTime(2026, 10, 26) && novMon[0].DayOfWeek == DayOfWeek.Monday && novMon.Contains(new DateTime(2026, 11, 30)),
        "weeks from Monday: the grid starts on Monday 26 Oct", novMon[0].ToString("d"));
    var feb = CalendarOps.MonthGrid(2027, 2, 0);
    ok(feb.Count(d => d.Month == 2) == 28 && feb.Distinct().Count() == 42, "February 2027: 28 days, 42 different days in the grid");
    ok(CalendarOps.WeekDays(0)[0] == DayOfWeek.Sunday && CalendarOps.WeekDays(1)[0] == DayOfWeek.Monday && CalendarOps.WeekDays(1)[6] == DayOfWeek.Sunday,
        "week day columns follow the week start");
    ok(CalendarOps.NormalizeWeekStart(5) == 0 && CalendarOps.NormalizeWeekStart(1) == 1, "week start is Sunday unless it is Monday");

    // ── rows by day ──
    var now = new DateTime(2026, 11, 24, 10, 0, 0);
    var doc = Doc(8);
    Row(doc, 0, "Meeting", "2026-11-24 09:00");
    Row(doc, 1, "Report", "2026-11-24");                       // all day
    Row(doc, 2, "Call", "24/11/2569 14:30");                    // Buddhist year typed by a user
    Row(doc, 3, "Broken", "next tuesday");                      // unreadable: not on the calendar
    Row(doc, 4, "Sent", "2026-11-25 08:00", TaskStatuses.DoneCode);
    Row(doc, 5, "", "2026-11-26 13:00", detail: "only a detail\nsecond line");
    Row(doc, 6, "Line one\nline two", "2026-11-27 07:15");
    var day = CalendarOps.Day(doc, new DateTime(2026, 11, 24), now);
    ok(day.Select(i => i.Title).SequenceEqual(["Report", "Meeting", "Call"]), "a day lists its rows: all day first, then by time", string.Join(",", day.Select(i => i.Title)));
    ok(day[0] is { HasTime: false, Overdue: true, Today: true } && day[1] is { HasTime: true, Overdue: true } && day[2] is { Overdue: false, Today: true },
        "overdue / today like the table's colours (a date-only due is at 00:00)");
    ok(day.All(i => i.Row == doc.RowIds.IndexOf(i.RowId)), "each item knows its row");
    var week = CalendarOps.Items(doc, new DateTime(2026, 11, 22), new DateTime(2026, 11, 29), now);
    ok(week.Count == 6 && week.All(i => i.Title != "Broken"), "a week: every readable due date, the unreadable one left out", week.Count.ToString());
    ok(week.Single(i => i.Title == "Sent") is { Done: true, Overdue: false, Status: TaskStatuses.DoneCode }, "a done row is done, never overdue");
    ok(CalendarOps.Items(doc, new DateTime(2026, 11, 22), new DateTime(2026, 11, 29), now, hideDone: true).All(i => !i.Done), "hide done leaves them out");
    ok(week.Single(i => i.Row == 5).Title == "only a detail" && week.Single(i => i.Row == 5).Detail.StartsWith("only a detail", StringComparison.Ordinal),
        "no title: the first text of the row, on one line");
    ok(week.Single(i => i.Row == 6).Title == "Line one", "a title of several lines shows its first line");
    ok(CalendarOps.TitleOf(doc, 7) == "" && CalendarOps.TitleText(new CalendarItem { RowId = "x", Row = 7, Due = now }) == "(row 8, no title)", "an empty row has no title");
    var map = CalendarOps.ByDay(week);
    ok(map[new DateTime(2026, 11, 24)].Count == 3 && map.ContainsKey(new DateTime(2026, 11, 27)), "items grouped by day");

    doc.Reminders[doc.RowIdAt(0)] = new ReminderInfo { Offsets = [60, 15] };
    doc.RowStyles[doc.RowIdAt(1)] = new RowStyle { Color = RowStyleOps.Palette[1].Key };
    var styled = CalendarOps.Day(doc, new DateTime(2026, 11, 24), now);
    ok(styled[1].Advance.SequenceEqual([15, 60]) && styled[0].Color == RowStyleOps.Palette[1].Hex, "advance reminders and the row colour come along");

    // ── later rounds of repeating tasks ──
    var rep = Doc(4);
    Row(rep, 0, "Weekly", "2026-11-24 09:00");
    rep.Recurrence[rep.RowIdAt(0)] = Recurrence.Weekly;
    Row(rep, 1, "Daily", "2026-11-25 08:00");
    rep.Recurrence[rep.RowIdAt(1)] = Recurrence.Daily;
    Row(rep, 2, "Done daily", "2026-11-24 07:00", TaskStatuses.DoneCode);
    rep.Recurrence[rep.RowIdAt(2)] = Recurrence.Daily;
    var dec = CalendarOps.Items(rep, new DateTime(2026, 12, 1), new DateTime(2027, 1, 1), now);
    var weekly = dec.Where(i => i.Title == "Weekly").ToList();
    ok(weekly.All(i => i.Ghost) && weekly.Select(i => i.Date.Day).SequenceEqual([1, 8, 15, 22, 29]) && weekly.All(i => i.Due.TimeOfDay == new TimeSpan(9, 0, 0)),
        "a weekly task (overdue today) comes back every week from the next round on, at its time", string.Join(",", weekly.Select(i => i.Date.Day)));
    var nov24 = CalendarOps.Day(rep, new DateTime(2026, 11, 24), now);
    ok(nov24.Count(i => i.Title == "Weekly") == 1 && !nov24.First(i => i.Title == "Weekly").Ghost, "the row itself is not doubled by its rounds");
    var daily = CalendarOps.Items(rep, new DateTime(2026, 11, 24), new DateTime(2026, 11, 29), now).Where(i => i.Title == "Daily").ToList();
    ok(daily.Select(i => (i.Date.Day, i.Ghost)).SequenceEqual([(25, false), (26, true), (27, true), (28, true)]), "a daily task due tomorrow: the row, then a round every day",
        string.Join(",", daily.Select(i => i.Date.Day + (i.Ghost ? "g" : ""))));
    ok(!dec.Any(i => i.Title == "Done daily"), "a done repeating task has no more rounds (its next round is a row of its own)");
    ok(CalendarOps.Items(rep, new DateTime(2026, 12, 1), new DateTime(2027, 1, 1), now, ghosts: false).Count == 0, "rounds can be left out");
    var monthly = CalendarOps.Rounds(new DateTime(2026, 1, 31, 9, 0, 0), Recurrence.MonthlyPrefix + "31", new DateTime(2026, 1, 1), new DateTime(2026, 6, 1)).ToList();
    ok(monthly.Select(d => d.ToString("MM-dd")).SequenceEqual(["02-28", "03-31", "04-30", "05-31"]),
        "monthly on the 31st: the last day of shorter months, then the 31st again (like the table)", string.Join(",", monthly.Select(d => d.ToString("MM-dd"))));
    var anchorless = CalendarOps.Rounds(new DateTime(2026, 1, 31), Recurrence.MonthlyPrefix + "0", new DateTime(2026, 1, 1), new DateTime(2026, 4, 1)).ToList();
    ok(anchorless.Select(d => d.Day).SequenceEqual([28, 31]), "a monthly task without its day keeps the due day", string.Join(",", anchorless.Select(d => d.Day)));
    var many = Doc(1);
    Row(many, 0, "Every day", "2026-11-24 09:00");
    many.Recurrence[many.RowIdAt(0)] = Recurrence.Daily;
    ok(CalendarOps.Items(many, new DateTime(2026, 11, 1), new DateTime(2027, 11, 1), now).Count(i => i.Ghost) == CalendarOps.MaxGhostsPerRow,
        "rounds of one task are capped");

    // ── adding a row for a day ──
    ok(CalendarOps.DefaultTimeFor(new DateTime(2026, 11, 25), now) == new TimeSpan(9, 0, 0), "a new item on another day: 09:00");
    ok(CalendarOps.DefaultTimeFor(now.Date, now.Date.AddHours(8)) == new TimeSpan(9, 0, 0), "today before 09:00: 09:00");
    ok(CalendarOps.DefaultTimeFor(now.Date, now.Date.AddHours(10).AddMinutes(52)) == new TimeSpan(11, 0, 0), "today at 10:52: 11:00, so it is not overdue at once");
    ok(CalendarOps.DefaultTimeFor(now.Date, now.Date.AddHours(23).AddMinutes(30)) == new TimeSpan(23, 59, 0), "today at 23:30: 23:59");
    var add = NoteDocument.CreateDefault(); // four empty rows
    var first = CalendarOps.AddRow(add, new DateTime(2026, 11, 26), new TimeSpan(9, 0, 0), now);
    ok(first == 0 && add.Rows.Count == 4 && add.GetCell(0, add.DueColumnIndex()) == "2026-11-26 09:00", "a new note's empty rows are used first", $"row {first}");
    var second = CalendarOps.AddRow(add, new DateTime(2026, 11, 27), null, now);
    ok(second == 1 && add.GetCell(1, add.DueColumnIndex()) == "2026-11-27", "the next one takes the next empty row; no time = all day");
    for (var i = 0; i < 2; i++) CalendarOps.AddRow(add, new DateTime(2026, 11, 28), null, now);
    var fifth = CalendarOps.AddRow(add, new DateTime(2026, 11, 29), null, now);
    ok(fifth == 4 && add.Rows.Count == 5, "no empty row left: a new last row");
    var past = NoteDocument.CreateDefault();
    var pastRow = CalendarOps.AddRow(past, new DateTime(2026, 11, 20), new TimeSpan(9, 0, 0), now);
    ok(ReminderPlan.Next(past, pastRow, now) == null && ReminderPlan.Next(past, pastRow, now.AddDays(1)) == null,
        "a row put on a day that has passed does not pop a reminder");
    var soon = CalendarOps.AddRow(past, now.Date, new TimeSpan(11, 0, 0), now);
    ok(ReminderPlan.Next(past, soon, now) == null && ReminderPlan.Next(past, soon, now.Date.AddHours(11).AddMinutes(1)) is { Offset: 0 },
        "a row for later reminds at its time");
    ok(CalendarOps.SetTime(past, soon, new TimeSpan(8, 0, 0), now) && ReminderPlan.Next(past, soon, now) == null,
        "moving its time into the past from the calendar does not pop a reminder either");
    ok(CalendarOps.SetTime(past, soon, new TimeSpan(10, 30, 0), now) && ReminderPlan.Next(past, soon, now.Date.AddHours(10).AddMinutes(31)) is { Offset: 0 },
        "…and back to a time still to come, it reminds again");
    var gap = NoteDocument.CreateDefault();
    Row(gap, 3, "Last", "");
    ok(CalendarOps.AddRow(gap, new DateTime(2026, 11, 26), null, now) == 4, "empty rows above a filled one are left alone (only the run at the end is used)");
    var styledBlank = NoteDocument.CreateDefault();
    styledBlank.RowStyles[styledBlank.RowIdAt(3)] = new RowStyle { Highlight = true };
    ok(CalendarOps.TrailingBlankRow(styledBlank) == -1, "a highlighted row is not empty");
    var picture = NoteDocument.CreateDefault();
    ok(CalendarOps.TrailingBlankRow(picture, id => id == picture.RowIdAt(3)) == -1 && CalendarOps.TrailingBlankRow(picture, id => id == picture.RowIdAt(1)) == 2,
        "a row with a picture in a cell is not empty");
    var repeatBlank = NoteDocument.CreateDefault();
    repeatBlank.Recurrence[repeatBlank.RowIdAt(3)] = Recurrence.Daily;
    ok(CalendarOps.TrailingBlankRow(repeatBlank) == -1, "a row set to repeat is not empty");
    var withDays = NoteDocument.CreateDefault();
    withDays.AddColumn("Days");
    ColumnSettingsOps.Apply(withDays, withDays.Columns.Count - 1, "Days", ColumnTypes.Days, null, null);
    DaysCounter.StartToday(withDays, withDays.Columns.Count - 1, now.AddDays(-30));
    var daysRow = CalendarOps.AddRow(withDays, new DateTime(2026, 11, 26), null, now);
    ok(daysRow == 0, "a days counter's start date does not make a row full");
    var full = NoteDocument.CreateDefault();
    full.Rows.Clear();
    full.RowIds.Clear();
    for (var i = 0; i < TableLimits.MaxRows; i++) { full.AppendRow(); full.SetCellRaw(i, 0, "x"); }
    ok(CalendarOps.AddRow(full, now, null, now) == -1 && full.Rows.Count == TableLimits.MaxRows, "a full table gets no row");
    var noDue = NoteDocument.CreateDefault();
    noDue.RemoveColumn(noDue.DueColumnIndex());
    ok(!CalendarOps.CanShow(noDue) && CalendarOps.AddRow(noDue, now, null, now) == -1 && CalendarOps.Items(noDue, now, now.AddDays(9), now).Count == 0,
        "without a due date column the calendar shows nothing and adds nothing");

    // ── moving, time, removing the date ──
    var mv = Doc(3);
    Row(mv, 0, "Timed", "2026-11-24 09:30");
    Row(mv, 1, "All day", "2026-11-24");
    var id0 = mv.RowIdAt(0);
    mv.Reminders[id0] = new ReminderInfo { Offsets = [15], Acked = [ReminderPlan.FireKey(0, new DateTime(2026, 11, 24, 9, 30, 0))] };
    mv.Recurrence[id0] = Recurrence.MonthlyPrefix + "24";
    ok(CalendarOps.Move(mv, 0, new DateTime(2026, 11, 30), now) && mv.GetCell(0, mv.DueColumnIndex()) == "2026-11-30 09:30", "moving keeps the time");
    ok(mv.Reminders[id0].Acked.Count == 0 && mv.Reminders[id0].Offsets.SequenceEqual([15]), "…clears the old acknowledgements, keeps the advance reminders");
    ok(mv.Recurrence[id0] == Recurrence.MonthlyPrefix + "30", "…and a monthly repeat takes the new day");
    ok(CalendarOps.Move(mv, 1, new DateTime(2026, 12, 1), now) && mv.GetCell(1, mv.DueColumnIndex()) == "2026-12-01", "an all-day item stays all day");
    ok(!CalendarOps.Move(mv, 1, new DateTime(2026, 12, 1), now), "moving to the same day changes nothing");
    ok(CalendarOps.SetTime(mv, 0, new TimeSpan(14, 5, 0), now) && mv.GetCell(0, mv.DueColumnIndex()) == "2026-11-30 14:05", "a new time keeps the day");
    ok(CalendarOps.SetTime(mv, 0, null, now) && mv.GetCell(0, mv.DueColumnIndex()) == "2026-11-30", "no time: all day");
    mv.SetCellRaw(2, mv.DueColumnIndex(), "30/11/2026 14:05");
    ok(!CalendarOps.SetTime(mv, 2, new TimeSpan(14, 5, 0), now) && mv.GetCell(2, mv.DueColumnIndex()) == "30/11/2026 14:05",
        "the same moment typed another way is not rewritten (no reset of its reminders)");
    var mvRows = mv.Rows.Count;
    ok(CalendarOps.ClearDate(mv, 0, now) && mv.GetCell(0, mv.DueColumnIndex()) == "" && mv.Rows.Count == mvRows && !CalendarOps.ClearDate(mv, 0, now),
        "removing the date empties the due cell; the row stays");
    ok(CalendarOps.TryParseTime("9:30", out var t1) && t1 == new TimeSpan(9, 30, 0) && CalendarOps.TryParseTime(" 23:59 ", out var t2) && t2 == new TimeSpan(23, 59, 0)
       && CalendarOps.TryParseTime("", out var t3) && t3 == null, "times: 9:30, 23:59, empty = all day");
    ok(!CalendarOps.TryParseTime("25:00", out _) && !CalendarOps.TryParseTime("9", out _) && !CalendarOps.TryParseTime("nine", out _) && !CalendarOps.TryParseTime("1.2:00", out _),
        "not times: 25:00, 9, nine");

    // ── rows written before the calendar: dates inside their text ──
    bool Found(string text, DateTime want, bool time) => DueDates.FindIn(text, out var got, out var t) && got == want && t == time;
    ok(Found("15/12/2026", new DateTime(2026, 12, 15), false) && Found("ส่ง 15/12/2569 14:30 นะ", new DateTime(2026, 12, 15, 14, 30, 0), true),
        "a date in a cell or inside text (B.E. year too), with its time");
    ok(Found("ประชุม 2027-01-05", new DateTime(2027, 1, 5), false) && Found("ครบ 5.1.2027", new DateTime(2027, 1, 5), false), "2027-01-05 and 5.1.2027");
    ok(Found("นัด 5 ม.ค. 2570", new DateTime(2027, 1, 5), false) && Found("งานวันที่ 3 มีนาคม พ.ศ. 2570 เวลา 9.30 น.", new DateTime(2027, 3, 3), false)
       && Found("5 ม.ค. 2570 9.30 น.", new DateTime(2027, 1, 5, 9, 30, 0), true), "Thai month names (with พ.ศ. and 9.30 น.)");
    ok(Found("Meeting Jan 5, 2026", new DateTime(2026, 1, 5), false) && Found("due 7 March 2026", new DateTime(2026, 3, 7), false), "English month names");
    ok(Found("from 1/12/2026 to 5/12/2026", new DateTime(2026, 12, 1), false), "the first date written is the one");
    ok(!DueDates.FindIn("โทร 081-234-5678", out _, out _) && !DueDates.FindIn("รุ่น 2.6.1.1", out _, out _) && !DueDates.FindIn("ครึ่งหนึ่ง 1/2", out _, out _)
       && !DueDates.FindIn("ราคา 12.50 บาท", out _, out _) && !DueDates.FindIn("", out _, out _), "not dates: a phone number, a version, 1/2, a price");
    var old = Doc(8);
    old.AddColumn("วันที่");
    var dateCol = old.Columns.Count - 1;
    Row(old, 0, "Has a due date", "2026-12-15 10:00");
    Row(old, 1, "No date", "");
    Row(old, 2, "Pay 20/12/2569", "");
    Row(old, 3, "Unreadable", "next week");
    Row(old, 4, "Dentist", "");
    old.SetCellRaw(4, dateCol, "5 ม.ค. 2570 9.30 น.");
    Row(old, 5, "Done, no date", "", TaskStatuses.DoneCode);
    var un = CalendarOps.Unplaced(old);
    ok(un.Select(u => u.Row).SequenceEqual([1, 2, 3, 4, 5]) && CalendarOps.UnplacedCount(old) == 5, "rows with text and no readable due date; empty rows and dated rows left out",
        string.Join(",", un.Select(u => u.Row)));
    ok(un[1].Found == new DateTime(2026, 12, 20) && !un[1].FoundTime && un[1].FoundIn == old.Columns[old.IndexOfRole(ColumnRoles.Title)], "a date in the title is found");
    ok(un[3].Found == new DateTime(2027, 1, 5, 9, 30, 0) && un[3].FoundTime && un[3].FoundIn == "วันที่", "…and in a column of dates, with its time");
    ok(un[2].Due == "next week" && un[2].Found == null && un[0].Found == null && un[4].Done, "an unreadable due date is shown; done rows are listed as done");
    var placed = CalendarOps.Place(old, [(un[0].RowId, new DateTime(2026, 12, 1), new TimeSpan(9, 0, 0)), (un[1].RowId, un[1].Found!.Value, null),
        (un[3].RowId, un[3].Found!.Value.Date, un[3].Found!.Value.TimeOfDay), ("gone", new DateTime(2026, 12, 1), null)], now);
    ok(placed == 3 && old.GetCell(1, old.DueColumnIndex()) == "2026-12-01 09:00" && old.GetCell(2, old.DueColumnIndex()) == "2026-12-20"
       && old.GetCell(4, old.DueColumnIndex()) == "2027-01-05 09:30", "Place puts each row on its day and time; a row that is gone is skipped");
    ok(CalendarOps.UnplacedCount(old) == 2 && CalendarOps.Items(old, new DateTime(2026, 12, 1), new DateTime(2027, 2, 1), now).Count(i => !i.Ghost) == 4,
        "now they are on the calendar");
    var earlier = CalendarOps.Unplaced(old).First(u => u.Row == 3);
    CalendarOps.Place(old, [(earlier.RowId, new DateTime(2026, 10, 1), new TimeSpan(9, 0, 0))], now);
    ok(ReminderPlan.Next(old, 3, now) == null, "an old row put on a past day does not pop a reminder");

    // ── the wallpaper corner ──
    ok(CalendarOps.CornerBox(new PixelRect(0, 0, 1920, 1040), 300, 340, 18) == new PixelRect(1602, 682, 300, 340),
        "bottom right of the work area, above the taskbar", CalendarOps.CornerBox(new PixelRect(0, 0, 1920, 1040), 300, 340, 18).ToString());
    ok(CalendarOps.CornerBox(new PixelRect(1920, 0, 2560, 1400), 450, 510, 27) == new PixelRect(4003, 863, 450, 510), "a second monitor (and its scale)");
    ok(CalendarOps.CornerBox(new PixelRect(0, 0, 400, 300), 600, 500, 10) == new PixelRect(10, 10, 380, 280), "a card larger than the screen is cut to it");

    // ── what comes next (the wallpaper's list) ──
    var up = Doc(8);
    Row(up, 0, "Late 1", "2026-11-20 09:00");
    Row(up, 1, "Late 2", "2026-11-22 09:00");
    Row(up, 2, "Late 3", "2026-11-23 09:00");
    Row(up, 3, "Today later", "2026-11-24 16:00");
    Row(up, 4, "Tomorrow", "2026-11-25 09:00");
    Row(up, 5, "Done today", "2026-11-24 18:00", TaskStatuses.DoneCode);
    Row(up, 6, "Far away", "2027-03-01 09:00");
    var next = CalendarOps.Upcoming(up, now, 4, out var more);
    ok(next.Select(i => i.Title).SequenceEqual(["Late 1", "Late 2", "Today later", "Tomorrow"]) && more == 1,
        "overdue first (two at most), then what comes next; done and far away left out", string.Join(",", next.Select(i => i.Title)) + " +" + more);
}

static void Texts(Check ok)
{
    Localizer.SetLanguage("en");
    var d = new DateTime(2026, 11, 24);
    ok(CalendarOps.MonthTitle(2026, 11) == "November 2026", "English month title", CalendarOps.MonthTitle(2026, 11));
    ok(CalendarOps.DayTitle(d) == "Tuesday, 24 November 2026", "English day title", CalendarOps.DayTitle(d));
    ok(CalendarOps.ShortDate(d) == "Tue 24 Nov" && CalendarOps.When(d, d.AddHours(8)) == "Today" && CalendarOps.When(d.AddDays(1), d) == "Tomorrow", "English short dates");
    ok(CalendarOps.Summary([]) == "Nothing due", "an empty day");
    Localizer.SetLanguage("th");
    ok(CalendarOps.MonthTitle(2026, 11) == "พฤศจิกายน 2026 (พ.ศ. 2569)", "Thai month title with the Buddhist year (dates stay CE)", CalendarOps.MonthTitle(2026, 11));
    ok(CalendarOps.DayTitle(d) == "วันอังคารที่ 24 พฤศจิกายน 2026 (พ.ศ. 2569)", "Thai day title", CalendarOps.DayTitle(d));
    ok(CalendarOps.ShortDate(d) == "อ. 24 พ.ย." && CalendarOps.When(d.AddDays(-1), d) == "เมื่อวาน", "Thai short dates", CalendarOps.ShortDate(d));
    ok(string.Join(" ", CalendarOps.WeekDays(0).Select(CalendarOps.WeekdayShort)) == "อา จ อ พ พฤ ศ ส", "Thai week day heads");
    var items = new List<CalendarItem>
    {
        new() { RowId = "a", Row = 0, Due = d, Done = true },
        new() { RowId = "b", Row = 1, Due = d, Overdue = true },
        new() { RowId = "c", Row = 2, Due = d },
        new() { RowId = "c", Row = 2, Due = d.AddDays(7), Ghost = true },
    };
    ok(CalendarOps.Summary(items) == "3 งาน · เสร็จแล้ว 1 · เลยกำหนด 1", "Thai summary (rounds not counted)", CalendarOps.Summary(items));
    var tip = CalendarOps.Describe(new CalendarItem
    {
        RowId = "x", Row = 4, Due = d.AddHours(9), HasTime = true, Title = "ประชุม", Detail = "ห้อง 3\nชั้น 2", Status = TaskStatuses.DoingCode,
        Repeat = Recurrence.Weekly, Advance = [15, 1440]
    });
    ok(tip.Contains("09:00  ประชุม") && tip.Contains("สถานะ: กำลังทำ") && tip.Contains("🔁 ทุกสัปดาห์") && tip.Contains("⏰ ก่อน 15 นาที, ก่อน 1 วัน")
       && tip.Contains("แถว 5 ในตาราง") && tip.Contains("ห้อง 3 ชั้น 2"), "the details of an item (tool tip)", tip.Replace("\n", " | "));
    Localizer.SetLanguage("en");
}

static void Settings(Check ok, string root)
{
    var dir = Path.Combine(root, "settings");
    Directory.CreateDirectory(dir);
    var note = Path.Combine(dir, "note.json");
    var s = AppSettings.Load(note);
    ok(s.CalendarPrefs == new CalendarPrefs(CalendarWallpaper.Off, 0, false), "defaults: no calendar on the wallpaper, weeks from Sunday, done rows shown");
    s.Save(note);
    var text = File.ReadAllText(Path.Combine(dir, "settings.json"));
    ok(!text.Contains("calendar", StringComparison.Ordinal), "defaults are not written", text);
    s.CalendarWallpaper = CalendarWallpaper.Corner;
    s.CalendarWeekStart = 1;
    s.CalendarHideDone = true;
    s.Save(note);
    text = File.ReadAllText(Path.Combine(dir, "settings.json"));
    ok(text.Contains("\"calendarWallpaper\": \"corner\"") && text.Contains("\"calendarWeekStart\": 1") && text.Contains("\"calendarHideDone\": true"), "settings.json keeps them", text);
    ok(AppSettings.Load(note).CalendarPrefs == new CalendarPrefs(CalendarWallpaper.Corner, 1, true), "…and reads them back");
    s.CalendarWallpaper = CalendarWallpaper.Only;
    s.Save(note);
    ok(AppSettings.Load(note).CalendarWallpaper == CalendarWallpaper.Only, "calendar only");
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"calendarWallpaper\": \"sideways\", \"calendarWeekStart\": 4, \"calendarHideDone\": \"yes\" }");
    var odd = AppSettings.Load(note);
    ok(odd.CalendarPrefs == new CalendarPrefs(CalendarWallpaper.Off, 0, false) && odd.Language == "th", "unknown values fall back to the defaults, the rest is kept");
}

static void Card(Check ok, string root)
{
    Localizer.SetLanguage("th");
    var now = DateTime.Now;
    var doc = Doc(6);
    Row(doc, 0, "งานเลยกำหนด", DueDates.Format(now.AddDays(-2)));
    Row(doc, 1, "ประชุมวันนี้", DueDates.Format(now.Date.AddHours(23).AddMinutes(30)));
    Row(doc, 2, "ส่งรายงาน", DueDates.FormatDate(now.Date.AddDays(1)));
    var card = CalendarImage.BuildCard(doc, now, new CalendarPrefs(CalendarWallpaper.Corner, 0, false), new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF6C2")));
    card.Measure(Size.Infinity);
    var size = card.DesiredSize;
    ok(size.Width is > 250 and < 340 && size.Height is > 200 and < 520, "the wallpaper calendar is a small card", size.ToString());
    var texts = card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    ok(texts.Contains(CalendarOps.MonthTitle(now.Year, now.Month)) && texts.Contains("อา") && texts.Contains("ส"), "month title and week days", string.Join(" | ", texts.Take(10)));
    ok(texts.Take(3).SequenceEqual([now.Day.ToString(System.Globalization.CultureInfo.InvariantCulture), "วัน" + CalendarOps.WeekdayName(now.DayOfWeek), CalendarOps.MonthTitle(now.Year, now.Month)]),
        "today in large at the top: the day, its name, the month", string.Join(" | ", texts.Take(3)));
    ok(texts.Contains("งานเลยกำหนด") && texts.Contains("ประชุมวันนี้") && texts.Contains("ส่งรายงาน") && texts.Contains("พรุ่งนี้") && texts.Contains("ทั้งวัน"),
        "what is overdue and comes next, with when and the time", string.Join(" | ", texts));
    var empty = CalendarImage.BuildCard(Doc(2), now, new CalendarPrefs(CalendarWallpaper.Corner, 1, false), Avalonia.Media.Brushes.White);
    var emptyTexts = empty.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    ok(emptyTexts.Contains(Localizer.T("calendar.wall_nothing")) && emptyTexts.IndexOf("จ") < emptyTexts.IndexOf("อา"), "nothing due: a short line; weeks from Monday");
    Localizer.SetLanguage("en");
}

static NoteDocument DocOf(MainWindow w)
{
    var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    return (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
}

static string? UndoLabel(MainWindow w)
{
    var undo = (UndoStack)typeof(MainWindow).GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    return undo.LastUndoLabel;
}

static object? Call(object target, string method, params object?[] args) =>
    target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

static void Pump(int ms = 60)
{
    var until = DateTime.Now.AddMilliseconds(ms);
    do
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Thread.Sleep(5);
    } while (DateTime.Now < until);
    Dispatcher.UIThread.RunJobs();
}

/// <summary>After an edit: the jobs it posted, then the calendar's (debounced) redraw at once, then its posted work.</summary>
static void Settle(MainWindow window)
{
    Pump(80);
    Call(window, "FlushCalendarRefresh");
    Pump(80);
}

static T Find<T>(Visual root, string name) where T : Control =>
    root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException(name + " not found");

static void Click(Window window, Control control)
{
    var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException((control.Name ?? control.GetType().Name) + " is not on the window");
    window.MouseMove(p);
    window.MouseDown(p, MouseButton.Left);
    window.MouseUp(p, MouseButton.Left);
}

static void Press(Window window, Key key, RawInputModifiers mods = RawInputModifiers.None)
{
    var physical = key switch
    {
        Key.Enter => PhysicalKey.Enter,
        Key.Escape => PhysicalKey.Escape,
        Key.Z => PhysicalKey.Z,
        Key.Y => PhysicalKey.Y,
        Key.Right => PhysicalKey.ArrowRight,
        Key.Left => PhysicalKey.ArrowLeft,
        Key.Down => PhysicalKey.ArrowDown,
        Key.PageDown => PhysicalKey.PageDown,
        Key.PageUp => PhysicalKey.PageUp,
        Key.Home => PhysicalKey.Home,
        Key.F9 => PhysicalKey.F9,
        _ => PhysicalKey.None
    };
    window.KeyPress(key, mods, physical, key == Key.Enter ? "\r" : "");
    window.KeyRelease(key, mods, physical, "");
}

static void Type(Window window, TextBox box, string text)
{
    box.Focus();
    Pump(30);
    box.SelectAll();
    window.KeyTextInput(text);
    Pump(30);
}

static List<string> TextsOf(Visual root) =>
    root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

static void Shot(Window window, string name)
{
    try
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var frame = window.CaptureRenderedFrame() ?? window.GetLastRenderedFrame();
        if (frame == null) return;
        var folder = Environment.GetEnvironmentVariable("TN_SHOTS") ?? Path.Combine(Path.GetTempPath(), "tn-calendar-shots");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, name + ".png"));
    }
    catch (Exception ex) { Console.WriteLine("screenshot " + name + " skipped: " + ex.Message); }
}

/// <summary>Text drawn with another font than the bundled one (colour emoji excepted).</summary>
static List<string> ForeignRuns(Visual root)
{
    var bad = new List<string>();
    foreach (var v in root.GetVisualDescendants().Prepend(root))
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
                if (run is Avalonia.Media.TextFormatting.ShapedTextRun sr && sr.GlyphRun.GlyphTypeface.FamilyName != "Noto Sans Thai")
                {
                    var text = new string(sr.Text.Span);
                    if (!text.EnumerateRunes().All(r => Rune.IsWhiteSpace(r) || r.Value >= 0x1F000 || r.Value is 0x23F0 or 0xFE0F or 0x200D))
                        bad.Add(sr.GlyphRun.GlyphTypeface.FamilyName + "[" + text + "]");
                }
    }
    return bad;
}

static void Ui(Check ok, string root)
{
    var dir = Path.Combine(root, "ui");
    Directory.CreateDirectory(dir);
    var note = Path.Combine(dir, "note.json");
    Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
    Localizer.SetLanguage("th");

    var window = new MainWindow { Width = 1000, Height = 640 };
    window.Show();
    Pump(300);
    var doc = DocOf(window);
    var due = doc.DueColumnIndex();
    var titleId = doc.ColumnIdAt(doc.IndexOfRole(ColumnRoles.Title));
    var detailId = doc.ColumnIdAt(doc.IndexOfRole(ColumnRoles.Detail));
    var statusId = doc.ColumnIdAt(doc.StatusColumnIndex());

    var button = window.FindControl<Button>("CalendarButton")!;
    ok(button.Content as string == "ปฏิทิน" && button.IsVisible, "the toolbar has a Calendar button (Thai)", button.Content as string ?? "");
    Click(window, button);
    Pump(300);
    var cal = window.Calendar;
    ok(cal is { IsVisible: true }, "Calendar opens the calendar window");
    if (cal == null) return;
    var now = DateTime.Now;
    ok(cal.TitleText == CalendarOps.MonthTitle(now.Year, now.Month) && cal.TitleText.Contains("พ.ศ. " + (now.Year + 543)), "this month, in Thai with the Buddhist year", cal.TitleText);
    ok(cal.CellFor(now.Date) != null && cal.CellFor(CalendarOps.GridStart(now.Year, now.Month, 0)) != null, "42 day cells, today among them");
    ok(ForeignRuns(cal).Count == 0, "every text in the calendar uses the bundled font", string.Join(" | ", ForeignRuns(cal).Take(6)));

    // A click on a day opens its pop-up table.
    var day = now.Date.AddDays(2);
    cal.ShowDate(day);
    Pump(100);
    var cell = cal.CellFor(day)!;
    Click(cal, cell);
    Settle(window);
    var popup = cal.Day;
    ok(popup is { IsVisible: true } && popup.Date == day, "clicking a day opens its pop-up table");
    if (popup == null) return;
    ok(popup.HeadingText.StartsWith(CalendarOps.DayTitle(day), StringComparison.Ordinal), "the pop-up says which day", popup.HeadingText);
    ok(popup.Rows.Count == 0 && TextsOf(popup).Contains(Localizer.T("calendar.day_empty")), "an empty day says how to add a row");

    // + Row: a row of the table, due that day at 09:00 (the first of the new note's empty rows).
    Click(popup, Find<Button>(popup, "DayAddRow"));
    Pump(300);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddHours(9)) && doc.Rows.Count == 4, "+ Row writes a row due that day into the table (09:00, first empty row)", doc.GetCell(0, due));
    ok(UndoLabel(window) == Localizer.T("undo.calendar_add"), "…as one undo step", UndoLabel(window) ?? "");
    var rowId = doc.RowIdAt(0);
    ok(popup.Rows.Count == 1 && popup.Rows[0].RowId == rowId && popup.SelectedRow == rowId, "the pop-up shows the new row, selected");
    var titleBox = popup.Editor(rowId, titleId) as TextBox;
    ok(titleBox != null && TopLevel.GetTopLevel(popup)?.FocusManager?.GetFocusedElement() == titleBox, "the title cell has the focus, ready to type");
    if (titleBox == null) return;
    Type(popup, titleBox, "ประชุมลูกค้า");
    Press(popup, Key.Enter);
    Settle(window);
    ok(doc.GetCell(0, doc.IndexOfRole(ColumnRoles.Title)) == "ประชุมลูกค้า", "typing in the pop-up writes the table's cell (Enter keeps it)", doc.GetCell(0, 0));
    ok(UndoLabel(window) == Localizer.T("undo.edit_cell"), "…with an undo step of its own");
    var chips = TextsOf(cal.CellFor(day)!);
    ok(chips.Any(t => t == "09:00 ประชุมลูกค้า"), "the calendar's day shows it: time and title", string.Join(" | ", chips));

    // The table changes → the pop-up follows.
    doc.SetCellRaw(0, doc.IndexOfRole(ColumnRoles.Detail), "ห้องประชุม 3");
    Call(window, "ScheduleSave");
    Settle(window);
    ok((popup.Editor(rowId, detailId) as TextBox)?.Text == "ห้องประชุม 3", "a change in the table shows in the open pop-up");

    // Typing reaches the table at once (saved like the table's own typing); Esc puts it back; keeping it is one undo step.
    var detailCol = doc.IndexOfRole(ColumnRoles.Detail);
    var labelBefore = UndoLabel(window);
    var detailBox = (TextBox)popup.Editor(rowId, detailId)!;
    Type(popup, detailBox, "ห้อง 5");
    Pump(80);
    ok(doc.GetCell(0, detailCol) == "ห้อง 5" && UndoLabel(window) == labelBefore, "typing in the pop-up is in the table at once, before it is kept", doc.GetCell(0, detailCol));
    Press(popup, Key.Escape);
    Pump(80);
    ok(doc.GetCell(0, detailCol) == "ห้องประชุม 3" && popup.IsVisible && detailBox.Text == "ห้องประชุม 3", "Esc puts the text back, in the table too (the pop-up stays open)");
    Type(popup, detailBox, "ห้อง 7");
    Press(popup, Key.Enter);
    Settle(window);
    ok(doc.GetCell(0, detailCol) == "ห้อง 7" && UndoLabel(window) == Localizer.T("undo.edit_cell"), "kept: one undo step");
    Find<Button>(popup, "DayClose").Focus();
    Pump(30);
    Press(popup, Key.Z, RawInputModifiers.Control);
    Settle(window);
    ok(doc.GetCell(0, detailCol) == "ห้องประชุม 3" && (popup.Editor(rowId, detailId) as TextBox)?.Text == "ห้องประชุม 3", "Ctrl+Z takes the whole typed text back", doc.GetCell(0, detailCol));

    // The time box.
    var time = (TextBox)popup.Editor(rowId, CalendarDayDialog.TimeColumn)!;
    Type(popup, time, "14:30");
    Press(popup, Key.Enter);
    Settle(window);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddHours(14).AddMinutes(30)), "a new time in the pop-up is the row's due time", doc.GetCell(0, due));
    time = (TextBox)popup.Editor(rowId, CalendarDayDialog.TimeColumn)!;
    Type(popup, time, "25:99");
    Press(popup, Key.Enter);
    Pump(300);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddHours(14).AddMinutes(30)) && popup.MessageText.Contains("25:99") && (popup.Editor(rowId, CalendarDayDialog.TimeColumn) as TextBox)?.Text == "14:30",
        "a time that is not one is refused and put back", popup.MessageText);
    time = (TextBox)popup.Editor(rowId, CalendarDayDialog.TimeColumn)!;
    Type(popup, time, "");
    time.Text = "";
    Press(popup, Key.Enter);
    Pump(300);
    ok(doc.GetCell(0, due) == DueDates.FormatDate(day), "an empty time makes it all day", doc.GetCell(0, due));
    time = (TextBox)popup.Editor(rowId, CalendarDayDialog.TimeColumn)!;
    Type(popup, time, "9:30");
    Press(popup, Key.Enter);
    Pump(300);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddHours(9).AddMinutes(30)) && (popup.Editor(rowId, CalendarDayDialog.TimeColumn) as TextBox)?.Text == "09:30",
        "9:30 is read as 09:30");

    // Repeat and status, as in the table: done adds the next round.
    var repeat = (ComboBox)popup.Editor(rowId, "\u0001repeat")!;
    repeat.SelectedIndex = 2; // weekly
    Settle(window);
    ok(doc.Recurrence.TryGetValue(rowId, out var kind) && kind == Recurrence.Weekly, "🔁 in the pop-up sets the row's repeat");
    var weekLater = day.AddDays(7);
    cal.ShowMonth(new DateTime(weekLater.Year, weekLater.Month, 1));
    Pump(100);
    ok(TextsOf(cal.CellFor(weekLater)!).Any(t => t.Contains("🔁") && t.Contains("ประชุมลูกค้า")), "the calendar shows the coming round a week later (🔁)");
    var status = (ComboBox)popup.Editor(rowId, statusId)!;
    status.SelectedIndex = 2; // done
    Settle(window);
    var spawned = doc.Rows.Count == 5 ? doc.RowIdAt(1) : "";
    ok(TaskStatuses.IsDone(doc.GetCell(0, doc.StatusColumnIndex())) && doc.Rows.Count == 5 && doc.GetCell(1, due) == DueDates.Format(weekLater.AddHours(9).AddMinutes(30)),
        "status Done in the pop-up: done, and the next round is a new row a week later", doc.GetCell(1, due));
    ok(popup.Rows.Count == 1 && popup.Rows[0].Done, "the done row stays in the day's table (faded)");
    ok(TextsOf(cal.CellFor(weekLater)!).Any(t => t == "09:30 ประชุมลูกค้า"), "…and the new round is a real row on the calendar");

    // Ctrl+Z in the pop-up undoes in the table.
    Find<Button>(popup, "DayClose").Focus();
    Pump(30);
    Press(popup, Key.Z, RawInputModifiers.Control);
    Settle(window);
    ok(!TaskStatuses.IsDone(doc.GetCell(0, doc.StatusColumnIndex())) && doc.Rows.Count == 4 && !doc.RowIds.Contains(spawned), "Ctrl+Z in the pop-up undoes the last step of the table");
    ok(popup.MessageText.StartsWith(Localizer.T("ui.undo_done", "").TrimEnd(), StringComparison.Ordinal), "…and says so", popup.MessageText);
    Press(popup, Key.Y, RawInputModifiers.Control);
    Settle(window);
    ok(TaskStatuses.IsDone(doc.GetCell(0, doc.StatusColumnIndex())) && doc.Rows.Count == 5, "Ctrl+Y redoes it");
    Press(popup, Key.Z, RawInputModifiers.Control);
    Settle(window);

    // Move to another day, ◀ ▶, remove the date.
    var picker = Find<CalendarDatePicker>(popup, "DayMoveTo");
    ok(picker.IsEnabled, "Move to is there for the selected row");
    picker.SelectedDate = day.AddDays(1);
    Settle(window);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddDays(1).AddHours(9).AddMinutes(30)) && popup.Rows.Count == 0, "Move to moves the row to another day (same time)", doc.GetCell(0, due));
    ok(UndoLabel(window) == Localizer.T("undo.calendar_move") && popup.MessageText.Contains(CalendarOps.ShortDate(day.AddDays(1))), "…one undo step, and says where", popup.MessageText);
    Click(popup, Find<Button>(popup, "DayNext"));
    Pump(300);
    ok(popup.Date == day.AddDays(1) && popup.Rows.Count == 1 && cal.Selected == day.AddDays(1), "▶ goes to the next day; the calendar follows");
    Click(popup, Find<Button>(popup, "DayClearDate"));
    Pump(300);
    ok(doc.GetCell(0, due) == "" && doc.GetCell(0, doc.IndexOfRole(ColumnRoles.Title)) == "ประชุมลูกค้า" && popup.Rows.Count == 0,
        "Remove the date takes it off the calendar; the row stays in the table");
    Find<Button>(popup, "DayClose").Focus();
    Press(popup, Key.Z, RawInputModifiers.Control);
    Settle(window);
    ok(doc.GetCell(0, due) == DueDates.Format(day.AddDays(1).AddHours(9).AddMinutes(30)) && popup.Rows.Count == 1, "…and Ctrl+Z puts the date back");

    // − Row deletes the row from the table (after asking: it has text); Ctrl+Z brings it back.
    var rowsBefore = doc.Rows.Count;
    Click(popup, Find<Button>(popup, "DayDeleteRow"));
    Pump(300);
    var question = popup.OwnedWindows.FirstOrDefault();
    ok(question != null, "deleting a row with text asks first");
    if (question != null)
    {
        Click(question, question.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == Localizer.T("delete.button_ok")));
        Settle(window);
    }
    ok(doc.Rows.Count == rowsBefore - 1 && !doc.RowIds.Contains(rowId) && popup.Rows.Count == 0, "− Row deletes it from the table");
    Find<Button>(popup, "DayClose").Focus();
    Press(popup, Key.Z, RawInputModifiers.Control);
    Settle(window);
    ok(doc.RowIds.Contains(rowId) && popup.Rows.Count == 1, "Ctrl+Z brings the row back, on the calendar too");

    Shot(popup, "calendar-day-th");
    ok(ForeignRuns(popup).Count == 0, "every text in the pop-up uses the bundled font", string.Join(" | ", ForeignRuns(popup).Take(6)));

    // ↗ shows the row in the table and closes the pop-up.
    Click(popup, Find<Button>(popup, "DayGoTo"));
    Settle(window);
    var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    var activeRow = (int)session.GetType().GetProperty("ActiveRow")!.GetValue(session)!;
    ok(cal.Day == null && !popup.IsVisible, "↗ closes the pop-up");
    ok(window.IsVisible && activeRow == doc.RowIds.IndexOf(rowId), "…and the table shows the row selected", activeRow.ToString());

    // From the table: a row's menu → Show in calendar opens its day with the row selected.
    var menu = ((IEnumerable<Control>)Call(window, "CalendarRowMenu", rowId)!).OfType<MenuItem>().Single();
    ok(menu.Header as string == "ดูในปฏิทิน" && menu.IsEnabled, "a row's menu has Show in calendar");
    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Settle(window);
    ok(cal.Day is { } shown && shown.Date == day.AddDays(1) && shown.SelectedRow == rowId, "…which opens the row's day with the row selected");
    cal.Day?.Close();
    Pump(200);
    var empty = doc.RowIdAt(3);
    var none = ((IEnumerable<Control>)Call(window, "CalendarRowMenu", empty)!).OfType<MenuItem>().Single();
    ok(!none.IsEnabled, "a row with no due date: Show in calendar is off");

    // The calendar window: keys, hide done, week start, language.
    cal.ShowDate(now.Date);
    Pump(100);
    Find<Button>(cal, "CalendarPrev").Focus();
    Pump(30);
    Press(cal, Key.Right);
    Pump(50);
    ok(cal.Selected == now.Date.AddDays(1), "→ selects the next day");
    Press(cal, Key.PageDown);
    Pump(50);
    ok(cal.Month == new DateTime(now.Year, now.Month, 1).AddMonths(1), "PageDown: next month");
    Press(cal, Key.Home);
    Pump(50);
    ok(cal.Month == new DateTime(now.Year, now.Month, 1) && cal.Selected == now.Date, "Home: back to today");
    var wall = Find<ComboBox>(cal, "CalendarWallpaper");
    wall.SelectedIndex = 1;
    Pump(200);
    ok(File.ReadAllText(Path.Combine(dir, "settings.json")).Contains("\"calendarWallpaper\": \"corner\""), "the wallpaper choice is kept in settings.json");
    wall.SelectedIndex = 0;
    Pump(200);
    var weekStart = cal.GetVisualDescendants().OfType<ComboBox>().First(c => c != wall);
    weekStart.SelectedIndex = 1;
    Pump(200);
    var heads = cal.GetVisualDescendants().OfType<TextBlock>().Where(t => t.FontWeight == Avalonia.Media.FontWeight.SemiBold && t.FontSize == 13).Select(t => t.Text).Take(7).ToList();
    ok(heads.FirstOrDefault() == "จ" && heads.LastOrDefault() == "อา", "weeks can start on Monday", string.Join(",", heads));
    weekStart.SelectedIndex = 0;
    Pump(200);
    Shot(cal, "calendar-month-th");

    Call(window, "SwitchLanguage", "en");
    Settle(window);
    ok(cal.TitleText == CalendarOps.MonthTitle(now.Year, now.Month) && !cal.TitleText.Contains("B.E.") && cal.Title == "TableNote — Calendar", "a new language redraws the calendar", cal.TitleText);
    Shot(cal, "calendar-month-en");
    Call(window, "SwitchLanguage", "th");
    Pump(200);

    // F9 brings the calendar forward; closing it leaves the editor as it was.
    cal.Close();
    Pump(200);
    ok(window.Calendar == null, "closing the calendar");
    window.Focus();
    Press(window, Key.F9);
    Pump(300);
    ok(window.Calendar is { IsVisible: true }, "F9 opens it again");
    window.Calendar?.Close();
    Pump(100);
    window.Close();
}

static void Wall(Check ok, string root)
{
    var dir = Path.Combine(root, "wall");
    Directory.CreateDirectory(dir);
    var note = Path.Combine(dir, "note.json");
    Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100, \"wallpaperTipShown\": true, \"calendarWallpaper\": \"corner\" }");
    Localizer.SetLanguage("th");
    var seed = NoteDocument.CreateDefault();
    var now = DateTime.Now;
    Row(seed, 0, "งานค้าง", DueDates.Format(now.AddDays(-1)));
    Row(seed, 1, "ประชุม", DueDates.Format(now.Date.AddDays(1).AddHours(10)));
    seed.Window = new WindowMemory { X = 40, Y = 40, Width = 700, Height = 420, Layer = DesktopLayerKind.Desktop };
    NoteStore.Save(note, seed);

    // The way back must exist before the editor hides: the start-again signal (as WallpaperTests).
    var program = typeof(MainWindow).Assembly.GetType("TableNote.Program")!;
    var signal = new EditorSignal("TableNote.CalTest." + Guid.NewGuid().ToString("N")[..8]);
    signal.TryStart(() => { });
    program.GetField("_signal", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, signal);
    program.GetField("_signalStarted", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);

    var desk = new Desk();
    MainWindow.WallpaperHostOverride = () => desk;
    var window = new MainWindow { Width = 900, Height = 600 };
    window.Show();
    Pump(300);
    var screen = window.Screens!.All[0];
    var picture = Path.Combine(dir, "beach.png");
    using (var bmp = new SKBitmap(screen.Bounds.Width, screen.Bounds.Height))
    {
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor(20, 20, 220));
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(picture, data.ToArray());
    }
    desk.Monitors = [new WallpaperMonitor("MON-A", screen.Bounds, picture)];
    window.FindControl<Button>("WallpaperButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(500);
    ok(desk.Calls.Count == 1 && desk.Calls[0].StartsWith("set MON-A table-", StringComparison.Ordinal), "wallpaper mode sets the picture", string.Join(" | ", desk.Calls));
    var folder = WallpaperFiles.Folder(note);
    var work = screen.WorkingArea;
    var scale = screen.Scaling;
    using (var b = SKBitmap.Decode(File.ReadAllBytes(Path.Combine(folder, "table-1.png"))))
    {
        var margin = (int)Math.Round(18 * scale);
        var inside = b.GetPixel(work.Right - margin - 24, work.Bottom - margin - 4);
        var outside = b.GetPixel(work.Right - 3, work.Bottom - 3);
        ok(Near(inside, 0xFF, 0xF6, 0xC2), "the calendar card is at the bottom right of the screen", inside.ToString());
        ok(Near(outside, 20, 20, 220), "…a little in from the edge (the wallpaper shows around it)", outside.ToString());
        ok(Near(b.GetPixel(40 + 30, 40 + 6), 0xFF, 0xF6, 0xC2), "the table card is still where the window was");
        File.Copy(Path.Combine(folder, "table-1.png"), Path.Combine(Environment.GetEnvironmentVariable("TN_SHOTS") ?? Path.GetTempPath(), "calendar-wallpaper-corner.png"), overwrite: true);
    }

    // While on the wallpaper: the calendar opens from the tray, and its changes redraw the wallpaper.
    desk.Calls.Clear();
    window.OpenCalendar();
    Pump(300);
    var cal = window.Calendar!;
    ok(cal.IsVisible && !window.IsVisible, "the calendar opens while the editor stays in the tray");
    var only = Find<ComboBox>(cal, "CalendarWallpaper");
    only.SelectedIndex = 2;
    Pump(2400);
    ok(desk.Calls.Count == 1, "choosing Calendar only redraws the wallpaper", string.Join(" | ", desk.Calls));
    var latest = desk.Monitors[0].Picture;
    using (var b = SKBitmap.Decode(File.ReadAllBytes(latest)))
    {
        ok(Near(b.GetPixel(40 + 30, 40 + 6), 20, 20, 220), "calendar only: no table card", b.GetPixel(70, 46).ToString());
        ok(Near(b.GetPixel(work.Right - (int)Math.Round(18 * scale) - 24, work.Bottom - (int)Math.Round(18 * scale) - 4), 0xFF, 0xF6, 0xC2), "…the calendar stays");
        File.Copy(latest, Path.Combine(Environment.GetEnvironmentVariable("TN_SHOTS") ?? Path.GetTempPath(), "calendar-wallpaper-only.png"), overwrite: true);
    }
    desk.Calls.Clear();
    var doc = DocOf(window);
    cal.OpenDay(now.Date.AddDays(3));
    Pump(300);
    Click(cal.Day!, Find<Button>(cal.Day!, "DayAddRow"));
    Pump(2400);
    ok(doc.Rows.Any(r => r.Contains(DueDates.Format(now.Date.AddDays(3).AddHours(9)))) && desk.Calls.Count == 1, "a row added from the calendar redraws the wallpaper", string.Join(" | ", desk.Calls));
    cal.Day?.Close();
    Pump(100);
    only.SelectedIndex = 0;
    Pump(2400);
    using (var b = SKBitmap.Decode(File.ReadAllBytes(desk.Monitors[0].Picture)))
        ok(Near(b.GetPixel(work.Right - (int)Math.Round(18 * scale) - 24, work.Bottom - (int)Math.Round(18 * scale) - 4), 20, 20, 220), "Off: no calendar on the wallpaper");
    cal.Close();
    Call(window, "ShowEditor");
    Pump(300);
    ok(window.IsVisible && desk.Monitors[0].Picture == picture, "back to the editor: the user's wallpaper is back");
    MainWindow.WallpaperHostOverride = null;
    window.Close();
    signal.Dispose();
}

/// <summary>A table written before the calendar: one row with a due date, rows without, a date in a title, a date in a
/// column of its own, an unreadable date. They come onto the calendar from the calendar, a day and a row's menu.</summary>
static void Old(Check ok, string root)
{
    var dir = Path.Combine(root, "old");
    Directory.CreateDirectory(dir);
    var note = Path.Combine(dir, "note.json");
    Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
    Localizer.SetLanguage("th");
    var today = DateTime.Today;
    var a = today.AddDays(20);
    var b = today.AddDays(24);
    var c = today.AddDays(40);
    string Be(DateTime d) => $"{d.Day}/{d.Month}/{d.Year + 543}";
    var seed = NoteDocument.CreateDefault();
    while (seed.Rows.Count < 7) seed.AppendRow();
    seed.AddColumn("วันที่");
    Row(seed, 0, "ประชุมใหญ่", DueDates.Format(a.AddHours(10)));
    Row(seed, 1, "ซื้อของเข้าบ้าน", "");
    Row(seed, 2, "จ่ายค่าไฟ " + Be(b), "");
    Row(seed, 3, "โทรหาช่าง", "อาทิตย์หน้า");
    Row(seed, 4, "นัดหมอ", "");
    seed.SetCellRaw(4, seed.Columns.Count - 1, Be(c) + " 9:30");
    NoteStore.Save(note, seed);

    var window = new MainWindow { Width = 1000, Height = 640 };
    window.Show();
    Pump(300);
    var doc = DocOf(window);
    var due = doc.DueColumnIndex();
    window.OpenCalendar();
    Pump(300);
    var cal = window.Calendar!;
    var jump = Find<CalendarDatePicker>(cal, "CalendarJump");
    jump.SelectedDate = a;
    Pump(200);
    ok(cal.Month == new DateTime(a.Year, a.Month, 1) && jump.SelectedDate == null, "Go to… jumps to that month (and is ready for the next one)");
    ok(TextsOf(cal.CellFor(a)!).Any(t => t == "10:00 ประชุมใหญ่"), "a row written before, with a due date, is on the calendar by itself");
    ok(cal.PlaceButton.IsVisible && cal.PlaceButton.IsEnabled && cal.PlaceButton.Content as string == "ใส่แถวเดิม… (4)", "the calendar counts the rows not on it yet",
        cal.PlaceButton.Content as string ?? "");

    // From the calendar: the rows with a date written in them are ticked; each goes on its own date.
    Click(cal, cal.PlaceButton);
    Pump(400);
    var placing = cal.Placing;
    ok(placing is { IsVisible: true } && placing.Rows.Count == 4, "Put existing rows… lists them");
    if (placing == null) return;
    ok(placing.Rows.Where(r => r.Box.IsChecked == true).Select(r => r.Row.Row).SequenceEqual([2, 4]), "the rows with a date in them are ticked");
    ok(ForeignRuns(placing).Count == 0, "every text in it uses the bundled font", string.Join(" | ", ForeignRuns(placing).Take(6)));
    Shot(placing, "calendar-place-th");
    Click(placing, Find<Button>(placing, "PlaceGo"));
    Pump(400);
    ok(!placing.IsVisible && doc.GetCell(2, due) == DueDates.Format(b.AddHours(9)) && doc.GetCell(4, due) == DueDates.Format(c.AddHours(9).AddMinutes(30)),
        "each row is on the date written in it (the time written, else 09:00)", doc.GetCell(2, due) + " / " + doc.GetCell(4, due));
    ok(UndoLabel(window) == Localizer.T("undo.calendar_place"), "…as one undo step");
    Settle(window);
    ok(cal.Month == new DateTime(b.Year, b.Month, 1) && cal.Selected == b && cal.PlaceButton.Content as string == "ใส่แถวเดิม… (2)",
        "the calendar shows the first of them; two rows left", cal.PlaceButton.Content as string ?? "");

    // From a day: + Existing rows… puts a ticked row on that day.
    var d = today.AddDays(30);
    cal.ShowDate(d);
    cal.OpenDay(d);
    Pump(300);
    var day = cal.Day!;
    Click(day, Find<Button>(day, "DayAddExisting"));
    Pump(400);
    var pick = day.Placing;
    ok(pick != null && pick.Rows.Count == 2 && pick.Rows.All(r => r.Box.IsChecked != true), "+ Existing rows… in a day: the rows left, none ticked (no date in them)");
    if (pick != null)
    {
        pick.Rows.First(r => r.Row.Row == 1).Box.IsChecked = true;
        Pump(50);
        Click(pick, Find<Button>(pick, "PlaceGo"));
        Pump(400);
    }
    ok(doc.GetCell(1, due) == DueDates.Format(d.AddHours(9)) && day.Rows.Any(r => r.Row == 1), "the ticked row is on that day, in its table", doc.GetCell(1, due));
    day.Close();
    Pump(200);

    // From the table: a row's menu → Put on the calendar… (the row is ticked); here on a day picked, all day.
    var rowId = doc.RowIdAt(3);
    var put = ((IEnumerable<Control>)Call(window, "CalendarRowMenu", rowId)!).OfType<MenuItem>().Single();
    ok(put.Header as string == "ใส่ในปฏิทิน…" && put.IsEnabled, "a row without a date: Put on the calendar…");
    put.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Pump(500);
    var fromRow = cal.Placing;
    ok(fromRow != null && fromRow.Rows.Single(r => r.Box.IsChecked == true).Row.RowId == rowId, "…opens it with that row ticked");
    if (fromRow != null)
    {
        Find<CalendarDatePicker>(fromRow, "PlaceDate").SelectedDate = d.AddDays(1);
        Find<TextBox>(fromRow, "PlaceTime").Text = "";
        Click(fromRow, Find<Button>(fromRow, "PlaceGo"));
        Pump(400);
    }
    ok(doc.GetCell(3, due) == DueDates.FormatDate(d.AddDays(1)), "…on the day picked; an empty time = all day", doc.GetCell(3, due));
    Settle(window);
    ok(!cal.PlaceButton.IsEnabled && cal.PlaceButton.Content as string == "ใส่แถวเดิม… (0)", "every row with text is on the calendar now");
    Call(window, "DoUndo");
    Settle(window);
    ok(doc.GetCell(3, due) == "อาทิตย์หน้า" && cal.PlaceButton.Content as string == "ใส่แถวเดิม… (1)", "Ctrl+Z puts the old text back");

    // A table without the due date column: the calendar says so and adds it with one click.
    doc.RemoveColumn(doc.DueColumnIndex());
    Call(window, "Rebuild");
    Settle(window);
    var addDue = Find<Button>(cal, "CalendarAddDue");
    ok(addDue.IsEffectivelyVisible && !cal.PlaceButton.IsVisible, "no due date column: the calendar offers to add it");
    Click(cal, addDue);
    Settle(window);
    ok(doc.DueColumnIndex() >= 0 && !addDue.IsEffectivelyVisible && cal.PlaceButton.IsVisible, "…and adds it; the calendar works again");
    cal.Close();
    window.Close();
}

static bool Near(SKColor c, byte r, byte g, byte b, int tolerance = 24) =>
    Math.Abs(c.Red - r) <= tolerance && Math.Abs(c.Green - g) <= tolerance && Math.Abs(c.Blue - b) <= tolerance;

delegate void Check(bool ok, string name, string detail = "");

/// <summary>A desktop that only records what TableNote sets (see tests/WallpaperTests for the full fake).</summary>
sealed class Desk : IWallpaperHost
{
    public List<WallpaperMonitor> Monitors = [];
    public readonly List<string> Calls = [];
    public bool Supported => true;

    public WallpaperState? Read(out string? detail)
    {
        detail = null;
        return new WallpaperState { Monitors = Monitors.ToList(), Fit = WallpaperFit.Fill, BackgroundColor = 0x00202020, PerMonitor = true };
    }

    public bool SetPicture(string? monitorId, string path, out string? detail)
    {
        Calls.Add($"set {monitorId ?? "*"} {Path.GetFileName(path)}");
        detail = null;
        for (var i = 0; i < Monitors.Count; i++)
            if (monitorId == null || Monitors[i].Id == monitorId) Monitors[i] = Monitors[i] with { Picture = path };
        return true;
    }

    public bool ClearPicture(out string? detail) { detail = null; return true; }
    public bool SetSlideshow(IReadOnlyList<string> items, uint options, uint tick, out string? detail) { detail = null; return true; }
}
