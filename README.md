# TableNote

[ภาษาไทย](README.th.md) | **English**

A lightweight, Excel-like task table for Windows with due-date and advance reminders, built with Avalonia on .NET 10.

Current version: **2.6.0** (20 November 2026). The interface is English by default; switch to Thai in **View → Language**. English manual: [`docs/manual-en.md`](docs/manual-en.md).

## What is TableNote?

TableNote is a small editable table for keeping track of tasks. Each row is a task. A new note gets the columns Title, Detail, Status, and Due in the current interface language (a new note created in Thai gets หัวข้อ, รายละเอียด, สถานะ, กำหนดส่ง). Notes you already have keep their column names. TableNote shows a reminder popup with a sound when a task is due, and it can also remind you ahead of time. It can put the table on your desktop wallpaper, and it keeps running in the system tray so reminders keep working.

## Features

- **Wallpaper mode (2.6.0, Windows):** **Wallpaper** on the toolbar draws the table over your own wallpaper, full screen, where the window was. The editor goes to the tray. Click the tray icon or press Ctrl+Shift+E to edit again, and your own wallpaper comes back. See [Wallpaper mode](#wallpaper-mode).
- **Whole rows and columns (2.6.0):** click a row number or a column header to select the whole row or column; drag or Shift+click for several. Right-click: insert as many rows (above / below) or columns (left / right) as are selected, delete them, or **Insert copied** — copied rows come back with their row color, repeat and advance reminders, copied columns with their type and width; text from Excel goes in as new rows. Drag the ⋮⋮ grip beside a row number to move the selected rows. Double-click a header to rename it.
- **Help in topics (2.6.0):** **Help → Help** lists topics; click one to open it, click again to close it (Open all / Close all).
- **Excel-style cells (2.6.0):** a click selects a cell (filled gold), dragging or Shift selects a block, typing replaces the cell, double-click or F2 edits it, Delete clears, Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste blocks to and from Excel, Ctrl+A or the top-left corner selects everything, a click on an empty spot clears the selection. No fill handle.
- **Formula bar (2.6.0):** above the table: the selected cell's address (B3) and its whole text, to read or edit long text (no calculation).
- **Find (2.6.0):** Ctrl+F (or **Table → Find…**) finds text in the table: every match is marked, Enter / Shift+Enter go to the next / previous one.
- **Reminder sound (2.6.0):** **View → System → Reminder sound…**: mute, pick a Windows sound or your own .wav, set the volume (above 50 % it asks first), test it.
- **Days counter (2.6.0):** **Table → Add days counter column** shows in each row how many days the work has been open ("150 days"); a row set to Done stops counting ("42 days ✓"). Click the cell to see the start date, the status and since when, the last update and the row's history with dates and times (status changes, edits, and progress notes you type there). See [Days counter](#days-counter).
- **One toolbar row (2.6.0):** + Row, − Row, + Column, Colors, Hide done, Sort by due, A− / 100% / A+, Export and Wallpaper. Copy, image export, print, delete column, language, help and the system settings (always on top, lock position, launch at sign-in, mute) are in the menu bar.
- Editable table: add or remove rows and columns, rename column headers, paste from and copy to Excel.
- **Move and delete columns (2.4.0):** drag the ⋮⋮ grip on a column header, use the header's right-click menu (Move left / right / to first / to last), or press Alt+Shift+←/→. The header menu also deletes a column after a confirmation (the Due column cannot be deleted). Undo with Ctrl+Z.
- **Move rows (2.4.0):** drag the row number, use the row-number menu (Move up / down / to top / to bottom), or press Alt+Shift+↑/↓. When **Hide done** is on, moves skip hidden rows. Undo with Ctrl+Z.
- **Column settings (2.4.0):** right-click a header and choose **Column settings…** to set the name, width, and type (text, number, choice dropdown, or check ✓). Choice options are entered one per line. Changing the type does not alter the cell data. Status and Due columns can only change name and width.
- **Row colour and highlight (2.4.0):** right-click the row number to set one of 8 colours (or none), toggle highlight, or clear the style. The style follows the row when it is moved or sorted, is undoable with Ctrl+Z, and is saved immediately.
- **Alert boxes:** **Table → Add alert box** adds a box with a title and text you can edit. The alert panel above the table shows only while there are boxes; its **+ Alert** adds more. **Delete** asks for confirmation first (the same dialog as deleting a row or column); this can't be undone. Drag the ⋮⋮ grip to reorder, or use **Up** / **Down** (keyboard reachable, no drag required). The boxes and their order are saved in `note.json`. Each box has a stable id. This is separate from due-date reminders, and Ctrl+Z does not undo alert-box changes.
- Status dropdown with three values: ยังไม่เริ่ม (Not started), กำลังทำ (In progress), and เสร็จแล้ว (Done).
  - Only these exact words count as done: `เสร็จ`, `เสร็จแล้ว`, `done`, `complete`, `completed`, `✓`, `✔`.
  - `ยังไม่เสร็จ` (not yet done) and `ไม่เสร็จ` (not done) are **not** done.
- Due dates are set with a calendar and an `HH:mm` time box.
  - 📅 sets "now + 1 minute" on an empty cell, for testing.
  - Shift+click 📅 sets tomorrow at 09:00.
  - Dates are always read day/month/year, whatever the PC's language or calendar: `5/1/2026 9:00` is 5 January 2026, 09:00. Single-digit hours and seconds are accepted.
  - Buddhist Era years are converted automatically (for example, `09/11/2569` becomes 2026).
  - A ⚠ next to the due cell means the date can't be read, so that task won't remind.
- **Advance reminders (⏰, new in 2.2.0):**
  - Remind 15 minutes, 1 hour, or 1 day before the due time, or set a custom time in minutes, hours, or days (up to 30 days).
  - Up to 5 per task. Duplicate values count once.
  - The on-time reminder always stays.
  - Reminder times that have already passed when you set them are skipped.
  - An overdue task only shows the on-time reminder "ครบกำหนดแล้ว" (Due now). If it is more than 2 minutes late, the popup also says when it was missed. Missed advance reminders don't pop up.
  - If an advance reminder fires late (for example, TableNote was just started), the popup shows the real time left and "พลาดการเตือนเมื่อ …" (missed at …). An advance popup left open past the due time turns into the on-time reminder.
  - Changing the advance reminders keeps earlier acknowledgements. They are cleared only when you edit the due date, or switch a done task back to not done (reminders are then recalculated from that moment).
- Reminders are checked about every 30 seconds. Several tasks are combined into one popup that doesn't steal keyboard focus, and plays the Windows system sound.
  - Each task has **เลื่อน 10 นาที** (Snooze 10 min), **เสร็จแล้ว** (Done), and **รับทราบ** (Acknowledge).
  - Snooze applies only to that reminder time. If an advance reminder is snoozed past the due time, it merges into the on-time reminder.
  - **ปิด** (Close) acknowledges all remaining tasks.
  - **X** (window close button) snoozes the remaining tasks for 10 minutes.
  - Changing a due date clears its earlier snoozes and acknowledgements.
  - A **ปิดเสียงเตือน** (Mute reminder sound) option is available.
- Recurring tasks: none, daily, weekly, or monthly (🔁).
  - Marking one done keeps the row as history and inserts the next occurrence below it, with the same advance reminders.
  - A monthly task on the 31st uses the last day of shorter months and then goes back to the 31st (Jan 31 → Feb 28 → Mar 31).
  - A date-only due date stays date-only.
- Due-date row colors: light red for overdue, yellow for due today, and faded grey with strikethrough for done (custom row colours from 2.4.0 sit on top of these).
- Sort by due date and hide done tasks.
- Undo and redo up to 100 steps (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z). This covers:
  - typing in a cell (one step per edit) and header edits;
  - Excel paste;
  - adding or removing rows and columns (F5–F8);
  - status, due date, repeat, and advance-reminder changes.

  While you are typing in a cell, Ctrl+Z first undoes the text in that cell. Snooze, acknowledgement, hide-done, and mute are not part of undo.
- The due column can't be deleted, because reminders depend on it. You can rename it.
- Wallpaper mode (Windows), a system tray menu, launch at sign-in, and an `--edit` startup flag.
- Autosave about 1.5 seconds after you stop typing.
- The interface is English by default; switch to Thai in **View → Language** (saved in `settings.json`).
- Zoom 80–200% (A− / 100% / A+, Ctrl +/−/0, Ctrl+wheel). From 2.6.0 zoom scales the alert boxes and the table; the menu and toolbar keep their size.
- Export the table to .xlsx, .csv, or .txt (see [Exporting](#exporting)).

### Reminders keep running

- Closing the main window (X, Alt+F4, or **File → Close window**) hides TableNote to the system tray; reminders keep working. To quit, choose **File → Exit TableNote**, or right-click the tray icon and choose **Quit** (**ออก** in Thai). A one-time notice explains this. If the tray icon can't be created, closing the window quits TableNote.
- Taskbar and tray: while you edit the table, TableNote has an icon on the taskbar and in the tray. In wallpaper mode only the tray icon remains. **Edit table** in the tray menu, clicking the tray icon, or Ctrl+Shift+E brings the editor (and its taskbar icon) back, and the mode is kept after a restart.
- With **Launch at sign-in** turned on (the startup entry is named `TableNote`), TableNote starts hidden in the tray (`--startup`). If it was last in wallpaper mode, it draws the table on the wallpaper again instead.

## Download and install

- Download: [latest release](https://github.com/sevesghost/TableNote/releases/latest) (`TableNote.exe`)
- Requirements: Windows 10 or 11, 64-bit
- The Windows build is a single self-contained `TableNote.exe` (.NET single-file host with the app bundled), so you don't need to install .NET separately. Run it directly.
- To keep reminders working, turn on **View → System → Launch at sign-in**.

## Getting started

1. Run `TableNote.exe`.
2. Type the task in หัวข้อ (Title) and รายละเอียด (Detail).
3. Pick a date in กำหนดส่ง (Due) and type a time, or press 📅.
4. Choose a status. If needed, choose a repeat option and press ⏰ for advance reminders.
5. When a reminder pops up, choose Snooze 10 min, Done, Acknowledge, or Close.

The in-app Help (**Help → Help** in the menu bar, or Help / วิธีใช้ in the tray menu) shows the guide in the current language. The first line shows the version. The Thai manual is in [`docs/manual-th.md`](docs/manual-th.md).

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| F5 | Add row |
| F6 | Delete the selected rows (asks first when they have text) |
| F7 | Add column |
| F8 | Delete the selected columns |
| Click / drag a row number | Select whole rows (Shift+click extends); drag the ⋮⋮ grip beside the number to move them |
| Click / drag a column header | Select whole columns (Shift+click extends); double-click or F2 renames the column |
| Right-click a row number / header / cell | Insert rows or columns (as many as are selected), delete them, Insert copied |
| Alt+Shift+← / → | Move the active column left / right |
| Alt+Shift+↑ / ↓ | Move the active row up / down (skips hidden rows when Hide done is on) |
| Click / drag | Select a cell / a block of cells (filled gold) |
| Typing on a selected cell | Replaces its text (like Excel) |
| Double-click / F2 | Edit the text that is in the cell (caret at the end); Esc puts it back |
| ← → ↑ ↓ / Tab / Shift+Tab | Move to the next cell; with Shift, the arrows grow the block |
| Enter | Keep the edit and move to the cell below |
| Shift+Enter | Move to the cell above |
| Delete | Clear the selected cells (Ctrl+Z brings them back) |
| Ctrl+A / the top-left corner | Select every cell |
| Ctrl+C | Copy the selected cells (tab-separated, pastes into Excel as it looks) |
| Ctrl+X | Cut the selected cells |
| Ctrl+V | Paste: a block from Excel starts at the selected cell; one value fills every selected cell |
| Ctrl+F / F3 / Shift+F3 | Find in the table / next / previous (Esc closes) |
| Ctrl+Z | Undo (up to 100 steps; also Table → Undo) |
| Ctrl+Y / Ctrl+Shift+Z | Redo (also Table → Redo) |
| Alt+F4 / the window's X | Close the window; TableNote keeps running in the tray (File → Exit TableNote quits) |
| Ctrl+Shift+E | Back to the editor from wallpaper mode (Windows) |
| Ctrl++ / Ctrl+− / Ctrl+0 | Zoom in / zoom out / back to 100% |
| Ctrl+mouse wheel | Zoom in or out |
| Ctrl+Home / Ctrl+End | Go to the first cell / the last cell of the last row |

## Days counter

**Table → Add days counter column** adds a column (just before the status column) that counts the days since each row's start date. Rows that are not done start today and a new row starts on the day it is added; you can also turn an empty column into one with **Column settings… → Type: Days counter**. The cell shows "Today", "1 day", "150 days"; a row set to **Done** stops counting and shows how long it took ("42 days ✓").

Click a days cell (or press Space) to open its pop-up:

- how long the work has been open (or took), from which date to which;
- **Start date**: pick another day, or **Today** (one Ctrl+Z step);
- the status and since when, and the last update ("8 days ago");
- the history, newest first: status changes, edits of the row and **progress notes**, each with its date, time and the day since the start. Type a note and press Enter (or **Add note**) to record a progress report. **Edit** (or a double-click on the note) changes its text: Enter or **Save** keeps it, Esc or **Cancel** puts it back; the note keeps its date and time and shows when it was edited. ✕ then **Delete?** removes one.

History starts on the day the table gets a days column and is saved in `note.json` (`rowHistory`, by row id, at most 100 entries per row). Changes to one cell within 10 minutes are one entry, and typing a new row's first values is not listed (its status changes are). Ctrl+Z of an edit is recorded as another change; notes are not part of Ctrl+Z. Exports and Copy give the start date (`yyyy-MM-dd`); image export, print and the wallpaper show the days.

## Wallpaper mode

**Wallpaper** on the toolbar (or **File → Set as wallpaper**) puts the table on your desktop wallpaper (Windows):

- The picture is full screen. Your own wallpaper stays, and a card with the alert boxes and the table sits on it where the editor window was, at the window's size at most and at its zoom. Rows that don't fit are counted as **+ N more rows**.
- Only the monitor the window is on changes. With **Span** (one picture across all monitors) the table is drawn across all of them.
- The editor goes to the tray. The picture follows the table: a task marked done from a reminder, a task that turns overdue or due today, and a new day redraw it.
- Back to editing: click the tray icon, choose **Edit table** in the tray menu, press Ctrl+Shift+E, or start TableNote again. Your own wallpaper comes back at once. **Quit** in the tray menu puts it back too; the next start draws the table again.
- At sign-out with **Launch at sign-in** on, the table stays on the wallpaper and is redrawn at the next sign-in; with it off, your wallpaper is put back.
- A slideshow is started again when you leave. **Windows spotlight** can't be turned back on by a program: TableNote asks first, and when you leave, today's spotlight picture stays as a fixed picture (turn spotlight on again in Settings › Personalization › Background).
- If you pick another wallpaper while the table is on it, TableNote keeps yours and draws the table on it from then on.
- A wallpaper fixed by your organization (Group Policy) is not changed.
- Before it changes anything, TableNote keeps a copy of your wallpaper and what to put back in `%LOCALAPPDATA%\TableNote\wallpaper\` (`wallpaper.json`, `original-*`, and the table pictures `table-1.png` / `table-2.png`). After a crash, the next start uses it; `TableNote.exe --edit` puts your wallpaper back and opens the editor.

## Exporting

**Export** in the toolbar (or **Export…** in the tray menu) saves the table as:

- `.csv`: comma-separated, UTF-8 with BOM, RFC 4180 quoting.
- `.txt`: tab-separated, UTF-8 with BOM.
- `.xlsx`: an Excel workbook.

**Exports follow the interface language.** Status labels (Not started / In progress / Done), the extra **Repeat** column and its values, and the default file name (`TableNote-yyyy-MM-dd`) are written in the language selected in **View → Language** at the time you export. Exporting the same note in English and in Thai therefore gives different labels. Your own column names and cell text are exported as typed, and `note.json` is never changed by the language setting. If another tool reads the exported file, export in the same language every time.

Due dates are exported as `yyyy-MM-dd`, or `yyyy-MM-dd HH:mm` when a time is set, using the Gregorian calendar (never a Buddhist Era year) in every language. The date is written as text, not as an Excel date serial. Numbers always use `.` as the decimal point, whatever the PC's region settings.

**Leading zeros:**

- `.csv` and `.txt` keep the text exactly as typed (`0812345678`). Excel, however, drops leading zeros when it opens a CSV directly (`0812345678` becomes `812345678`). To keep them, import the file with **Data → From Text/CSV** and set that column to **Text**, or export to `.xlsx` instead.
- In `.xlsx`, a value is stored as a number only if it reads back exactly the same. Values with a leading zero, a `+` sign, trailing decimal zeros, more than 15 significant digits, or 12 or more digits before the decimal point (such as a 13-digit ID or an account number) are stored as text, so nothing is lost.

## Data storage

- Windows: `%APPDATA%\TableNote\note.json`
- Linux: `~/.local/share/TableNote/note.json`
- Or set the `TABLENOTE_DATA` environment variable to a full file path.
- Wallpaper mode (Windows): `%LOCALAPPDATA%\TableNote\wallpaper\`, or a `wallpaper` folder next to `note.json` when `TABLENOTE_DATA` is set.

Advance reminders are stored in a top-level `reminders` field keyed by row id, for example `{"<rowId>": {"offsets": [60, 15], "acked": [...]}}` (offsets are saved largest first). A wrongly typed value (for example `"offsets": ["15"]`) is dropped on its own and logged to `tablenote.log` next to `note.json`; the rest of the table is kept.

From 2.4.0 the save file is format version 5 and adds `columnIds` (stable ids parallel to the columns), `columnSettings` (column type and choice options, keyed by column id), and `rowStyles` (row colour and highlight, keyed by row id). Older files (v2–v4) open as usual; the first save backs them up as `note.vN.bak` (for example `note.v4.bak`). Files from a newer TableNote are not downgraded, and unknown fields are kept. A 2.3.x build can still open a v5 file: it does not reject it or drop the new fields (they are stored and written back on save, after a `note.v5.bak` backup) but it does not use column ids, column settings, or row styles.

Alert boxes are an optional `alertBoxes` array of `{ "id", "title", "text" }` in display order. The file format version stays 5. A missing `alertBoxes`, or one that is not an array, loads as no boxes. A bad entry is skipped on its own and logged. Unknown properties on a box (reserved for a later per-box colour) are written back. A build that does not know this field keeps it and writes it back.

Files from older versions open without data loss and get on-time reminders only. Before the first save in the 2.2 format, TableNote keeps a copy of the old file named after its version: `note.v2.bak` for 1.x/2.0 files and `note.v3.bak` for 2.1.0 files. Existing backups are never overwritten; if the name is taken, a timestamped name such as `note.v3.20261109141500.bak` is used. Invalid reminder values are dropped one by one without creating a backup. If the file is unreadable, TableNote starts a new table and keeps the old file as `note.json.bak`. If another program is using `note.json` at start-up (for example antivirus or a backup tool), TableNote shows an empty stand-in table, leaves `note.json` and the launch-at-sign-in setting alone, and keeps trying: it opens `note.json` as soon as it can be read. Anything typed into the stand-in table is saved to `note.unsaved.json` instead; the next start renames that file to `note.unsaved-<time>.json` (never overwritten or deleted) and says so. If the file is larger than 8 MB or has more than 1,000 rows, TableNote first copies the full file to `note.overlimit-<time>.bak`, then opens the table within the limits (the first 1,000 rows when the row limit is exceeded). If a save would make `note.json` larger than 8 MB, the last saved file is left unchanged and the current table is copied to `note.overlimit-<time>.bak`. From 2.3.0, only the 5 newest `note.json*.bak` files are kept (version backups and `note.overlimit-*.bak` are never deleted automatically). The log is written in English only and is capped at 512 KB (2 files: `tablenote.log` and `tablenote.1.log`).

Limits: 1,000 rows, 30 user columns (system columns can bring the total to 32), 2,000 characters per cell, 256 KB per paste, 5 advance reminders per task, 30 alert boxes (title 200 characters, text 2,000), and `note.json` up to 8 MB.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/sevesghost/TableNote.git
cd TableNote
dotnet restore
dotnet build -c Release
```

Publish for Windows (win-x64, self-contained, single file, trimmed; about 24 MB).

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/win-x64
```

The output is `publish/win-x64/TableNote.exe`.

Run on Linux (for UI testing only; this uses `net10.0`):

```bash
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish/linux-x64
dotnet ./publish/linux-x64/TableNote.dll
```

Source layout:

- `Core/`: table model, JSON storage, Excel paste and copy, recurrence, reminder scheduling (`ReminderPlan.cs`), and undo.
- `Platform/`: Windows wallpaper (IDesktopWallpaper), hotkey, taskbar, start-again signal, and autostart.
- `Services/`: reminder timer, popup, ⏰ dialog, and sound.
- `MainWindow.axaml(.cs)`: main UI.
- `Assets/Brand/`: the Table Note logo shown on the loading screen (`SplashWindow.cs`).
- `Assets/`: fonts (Noto Sans Thai with the Latin of Noto Sans and a few Noto symbols merged in, so Thai, digits and Latin share one design; rebuilt with `tools/fonts/merge_noto.py`) and tray icon.

Want to fix a bug or add a language? See [CONTRIBUTING.md](CONTRIBUTING.md).

## Version history

See [CHANGELOG.md](CHANGELOG.md).

## License

TableNote is released under the [MIT License](LICENSE). The bundled fonts (Noto Sans Thai, Noto Sans, Noto Sans Symbols, Noto Sans Symbols 2 and Noto Sans Math, merged) are under the [SIL Open Font License 1.1](Assets/Fonts/OFL.txt).
