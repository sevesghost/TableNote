# TableNote

[ภาษาไทย](README.th.md) | **English**

A lightweight, Excel-like task table for Windows with due-date and advance reminders, built with Avalonia on .NET 10.

Current version: **2.4.2** (12 November 2026). The interface is English by default; switch to Thai with the **EN/TH** button. English manual: [`docs/manual-en.md`](docs/manual-en.md).

## What is TableNote?

TableNote is a small editable table for keeping track of tasks. Each row is a task. A new note gets the columns Title, Detail, Status, and Due in the current interface language (a new note created in Thai gets หัวข้อ, รายละเอียด, สถานะ, กำหนดส่ง). Notes you already have keep their column names. TableNote shows a reminder popup with a sound when a task is due, and it can also remind you ahead of time. It can sit attached to your desktop background, and it keeps running in the system tray so reminders keep working.

## Features

- Editable table: add or remove rows and columns, rename column headers, paste from and copy to Excel.
- **Move and delete columns (2.4.0):** drag the ⋮⋮ grip on a column header, use the header's right-click menu (Move left / right / to first / to last), or press Alt+Shift+←/→. The header menu also deletes a column after a confirmation (the Due column cannot be deleted). Undo with Ctrl+Z.
- **Move rows (2.4.0):** drag the row number, use the row-number menu (Move up / down / to top / to bottom), or press Alt+Shift+↑/↓. When **Hide done** is on, moves skip hidden rows. Undo with Ctrl+Z.
- **Column settings (2.4.0):** right-click a header and choose **Column settings…** to set the name, width, and type (text, number, choice dropdown, or check ✓). Choice options are entered one per line. Changing the type does not alter the cell data. Status and Due columns can only change name and width.
- **Row colour and highlight (2.4.0):** right-click the row number to set one of 8 colours (or none), toggle highlight, or clear the style. The style follows the row when it is moved or sorted, is undoable with Ctrl+Z, and is saved immediately.
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
- Reminders are checked about every 30 seconds. Several tasks are combined into one popup that doesn't steal keyboard focus, and plays the Windows system sound. A silent Windows toast notification is also shown.
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
- Background mode attached to the desktop (Windows), a system tray menu, launch at sign-in, and an `--edit` startup flag.
- Autosave about 1.5 seconds after you stop typing.
- The interface is English by default; switch to Thai with the **EN/TH** button (saved in `settings.json`).
- Zoom 80–200% (A− / 100% / A+, Ctrl +/−/0, Ctrl+wheel).
- Export the table to .xlsx, .csv, or .txt (see [Exporting](#exporting)).

### Reminders keep running

- Closing the main window hides TableNote to the system tray; reminders keep working. To quit, right-click the tray icon and choose **Exit** (**ออก** in Thai). A one-time notice explains this. If the tray icon can't be created, closing the window quits TableNote.
- Taskbar and tray: while you edit the table, TableNote has an icon on the taskbar and in the tray. When it is attached to the desktop background, only the tray icon remains. **Edit table** in the tray menu, Ctrl+Shift+E, or detaching brings the taskbar icon back, and the mode is kept after a restart.
- With **Launch at sign-in** turned on (the startup entry is named `TableNote`), TableNote starts hidden in the tray (`--startup`). If it was last in background mode, it starts in background mode instead.

## Download and install

- Download: [latest release](https://github.com/sevesghost/TableNote/releases/latest) (`TableNote.exe`)
- Requirements: Windows 10 or 11, 64-bit
- The Windows build is a single self-contained `TableNote.exe` (.NET single-file host with the app bundled), so you don't need to install .NET separately. Run it directly.
- To keep reminders working, tick **Launch at sign-in** in the toolbar.

## Getting started

1. Run `TableNote.exe`.
2. Type the task in หัวข้อ (Title) and รายละเอียด (Detail).
3. Pick a date in กำหนดส่ง (Due) and type a time, or press 📅.
4. Choose a status. If needed, choose a repeat option and press ⏰ for advance reminders.
5. When a reminder pops up, choose Snooze 10 min, Done, Acknowledge, or Close.

The in-app Help (the **?** button on the toolbar, or Help / วิธีใช้ in the tray menu) shows the guide in the current language. The first line shows the version. The Thai manual is in [`docs/manual-th.md`](docs/manual-th.md).

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| F5 | Add row |
| F6 | Delete row |
| F7 | Add column |
| F8 | Delete column |
| Alt+Shift+← / → | Move the active column left / right |
| Alt+Shift+↑ / ↓ | Move the active row up / down (skips hidden rows when Hide done is on) |
| Enter | Move to the cell below |
| Shift+Enter | Move to the cell above |
| Ctrl+C | Copy selected cells (without headers) |
| Ctrl+V | Paste from Excel (plain text, formulas not evaluated) |
| Ctrl+Z | Undo (up to 100 steps) |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+Shift+E | Return to edit mode from background mode (Windows) |
| Ctrl++ / Ctrl+− / Ctrl+0 | Zoom in / zoom out / back to 100% |
| Ctrl+mouse wheel | Zoom in or out |
| Ctrl+Home / Ctrl+End | Go to the first cell / the last cell of the last row |

## Exporting

**Export** in the toolbar (or **Export…** in the tray menu) saves the table as:

- `.csv`: comma-separated, UTF-8 with BOM, RFC 4180 quoting.
- `.txt`: tab-separated, UTF-8 with BOM.
- `.xlsx`: an Excel workbook.

**Exports follow the interface language.** Status labels (Not started / In progress / Done), the extra **Repeat** column and its values, and the default file name (`TableNote-yyyy-MM-dd`) are written in the language selected with **EN/TH** at the time you export. Exporting the same note in English and in Thai therefore gives different labels. Your own column names and cell text are exported as typed, and `note.json` is never changed by the language setting. If another tool reads the exported file, export in the same language every time.

Due dates are exported as `yyyy-MM-dd`, or `yyyy-MM-dd HH:mm` when a time is set, using the Gregorian calendar (never a Buddhist Era year) in every language. The date is written as text, not as an Excel date serial. Numbers always use `.` as the decimal point, whatever the PC's region settings.

**Leading zeros:**

- `.csv` and `.txt` keep the text exactly as typed (`0812345678`). Excel, however, drops leading zeros when it opens a CSV directly (`0812345678` becomes `812345678`). To keep them, import the file with **Data → From Text/CSV** and set that column to **Text**, or export to `.xlsx` instead.
- In `.xlsx`, a value is stored as a number only if it reads back exactly the same. Values with a leading zero, a `+` sign, trailing decimal zeros, more than 15 significant digits, or 12 or more digits before the decimal point (such as a 13-digit ID or an account number) are stored as text, so nothing is lost.

## Data storage

- Windows: `%APPDATA%\TableNote\note.json`
- Linux: `~/.local/share/TableNote/note.json`
- Or set the `TABLENOTE_DATA` environment variable to a full file path.

Advance reminders are stored in a top-level `reminders` field keyed by row id, for example `{"<rowId>": {"offsets": [60, 15], "acked": [...]}}` (offsets are saved largest first). A wrongly typed value (for example `"offsets": ["15"]`) is dropped on its own and logged to `tablenote.log` next to `note.json`; the rest of the table is kept.

From 2.4.0 the save file is format version 5 and adds `columnIds` (stable ids parallel to the columns), `columnSettings` (column type and choice options, keyed by column id), and `rowStyles` (row colour and highlight, keyed by row id). Older files (v2–v4) open as usual; the first save backs them up as `note.vN.bak` (for example `note.v4.bak`). Files from a newer TableNote are not downgraded, and unknown fields are kept. A 2.3.x build can still open a v5 file: it does not reject it or drop the new fields (they are stored and written back on save, after a `note.v5.bak` backup) but it does not use column ids, column settings, or row styles.

Files from older versions open without data loss and get on-time reminders only. Before the first save in the 2.2 format, TableNote keeps a copy of the old file named after its version: `note.v2.bak` for 1.x/2.0 files and `note.v3.bak` for 2.1.0 files. Existing backups are never overwritten; if the name is taken, a timestamped name such as `note.v3.20261109141500.bak` is used. Invalid reminder values are dropped one by one without creating a backup. If the file is unreadable, TableNote starts a new table and keeps the old file as `note.json.bak`. If the file is larger than 8 MB or has more than 1,000 rows, TableNote first copies the full file to `note.overlimit-<time>.bak`, then opens the table within the limits (the first 1,000 rows when the row limit is exceeded). If a save would make `note.json` larger than 8 MB, the last saved file is left unchanged and the current table is copied to `note.overlimit-<time>.bak`. From 2.3.0, only the 5 newest `note.json*.bak` files are kept (version backups and `note.overlimit-*.bak` are never deleted automatically). The log is written in English only and is capped at 512 KB (2 files: `tablenote.log` and `tablenote.1.log`).

Limits: 1,000 rows, 30 user columns (system columns can bring the total to 32), 2,000 characters per cell, 256 KB per paste, 5 advance reminders per task, and `note.json` up to 8 MB.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/sevesghost/TableNote.git
cd TableNote
dotnet restore
dotnet build -c Release
```

Publish for Windows (win-x64, self-contained, single file). With `-r win-x64` the project targets `net10.0-windows10.0.19041.0`, which enables Windows toast notifications.

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
- `Platform/`: Windows desktop shell, hotkey, tray, and autostart.
- `Services/`: reminder timer, popup, ⏰ dialog, sound, and toast.
- `MainWindow.axaml(.cs)`: main UI.
- `Assets/`: Noto Sans Thai fonts and tray icon.

Want to fix a bug or add a language? See [CONTRIBUTING.md](CONTRIBUTING.md).

## Version history

See [CHANGELOG.md](CHANGELOG.md).

## License

TableNote is released under the [MIT License](LICENSE). The bundled Noto Sans Thai fonts are under the [SIL Open Font License 1.1](Assets/Fonts/OFL.txt).
