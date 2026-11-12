# TableNote User Guide 2.4.0

TableNote is a spreadsheet-style task list, similar to Excel, that reminds you when a task is due and can remind you in advance.

---

## 1. Getting started

1. Double-click `TableNote.exe`. You will see a light-yellow table with 4 columns: **Title, Details, Status, Due**. The interface is in English by default; switch to Thai with the **EN/TH** button (see section 11).
2. Click any cell and start typing. TableNote **saves automatically** about 1.5 seconds after you stop typing, so you never need to save by hand.
3. Only one TableNote window can be open at a time. If you launch it again, the existing window is used.
4. While you edit the table, TableNote has an icon on the taskbar and a **TableNote** icon in the system tray in the corner of the screen. When it is attached to the desktop background, only the tray icon remains.

> **Reminders work only while TableNote is running**
>
> - Clicking the main window's close button (X) does **not** quit TableNote. It minimizes to the tray icon and keeps reminding you. A message explains this the first time.
> - If there is no TableNote icon in the tray (for example, the system could not create it), clicking X really quits, and you won't get reminders until you open it again.
> - To bring the window back, click the tray icon or choose **Edit table**.
> - To really quit, right-click the tray icon and choose **Exit**. You won't get reminders until you open TableNote again.
> - We recommend ticking **Launch at sign-in**. TableNote then starts hidden in the tray every time you sign in. If you last used Pin to desktop mode, it starts in that mode instead.
> - For in-app help, click the **?** button on the toolbar or choose **Help** from the tray menu.

## 2. Adding tasks

| To do this | Toolbar button | Shortcut |
|---|---|---|
| Add a row | `+ Row` | F5 |
| Delete a row (asks first if it has text) | `− Row` | F6 |
| Add a column | `+ Column` | F7 |
| Delete a column (asks first if it has text; the Due column can't be deleted) | `− Column` | F8 |

Steps
1. Click **+ Row** or press F5.
2. Enter a **Title**, which is the name shown in the reminder window, and add **Details** if you like.
3. Pick a **Status** from the list. There are 3 values: `Not started`, `In progress`, `Done`.
4. Set the **Due** date and choose whether the task **repeats** (see the next section).

TableNote stores the status in your file as one of 3 language-neutral codes, `todo` (Not started), `doing` (In progress) and `done` (Done), and translates it only for display, so switching the language never changes your data or a task's status.
- When an older file is opened, only the standard labels are converted: `ยังไม่เริ่ม`, `ยังไม่เสร็จ` and `todo` become `todo`; `กำลังทำ` becomes `doing`; and `เสร็จ`, `เสร็จแล้ว`, `done` and `complete` become `done`. Other words, such as `completed`, `✓` and `✔`, are kept as typed but still count as done.
- A status you type yourself is kept exactly as you typed it. It counts as done only if it exactly matches one of these words: `เสร็จ`, `เสร็จแล้ว`, `done`, `complete`, `completed`, `✓`, `✔` (any letter case). Anything else counts as not done.

A status left empty is saved as empty, is shown as `Not started` (or `ยังไม่เริ่ม` in Thai), and counts as not done. TableNote doesn't fill it in for you.

Phrases such as `ไม่เสร็จ` or `เสร็จไม่ทัน` ("not finished", "late") **do not count as done**, so the task keeps reminding you as usual.

You can rename every column header, including "Status" and "Due", because TableNote remembers each column's role rather than reading its name. The Due column **can't be deleted** because reminders use it; trying to delete it shows a message. If a file has no Due column, a message appears right away saying reminders won't work.

A table can have up to 1,000 rows (raised from 200 in 2.3.0) and 30 user-added columns (system columns may add a few more). Each cell holds up to 2,000 characters.

## 3. Setting due dates and repeating tasks

The **Due** cell has 5 parts, side by side:

1. **Date box**: click it and pick a day from the calendar.
2. **Time box**: type a time as `HH:mm`, for example `14:30`. If you leave it empty, `09:00` is used.
3. **Repeat box**: choose `None`, `Daily`, `Weekly` or `Monthly`. Repeating tasks show 🔁 in front of the cell.
4. **📅 button**
   - Click: if the cell already has a date, the calendar opens so you can pick one. If the cell is empty, the due time is set to 1 minute from now right away (the calendar doesn't open). Use this to test that reminders work.
   - Hold Shift and click: sets the due time to tomorrow at 09:00.
5. **⏰ button**: sets advance reminders (see 3.1). Once set, it shows how many, for example ⏰2.

A ⚠ in front of the cell means TableNote can't read the due date in that cell, so that task won't remind you. Set the date again from the calendar.

### 3.1 Advance reminders ⏰

1. Click the ⏰ button in the task's Due cell.
2. Tick **15 minutes before**, **1 hour before**, **1 day before**, or enter a **Custom:** number and pick minutes / hours / days, then click **Add**.
3. Click **Save**.

Good to know
- You can set up to **5 per task**. Duplicate values count once, and an advance reminder can be at most 30 days before.
- **The on-time reminder is always there.** You don't need to add it.
- Reminder times that have already passed when you set them are skipped and don't pop up right away. For example, setting "1 day before" on a task due in 2 hours.
- A task that is already overdue (for example, when you turn on your computer) only gives the **Due now** reminder. Missed advance reminders don't pop up.
- For a repeating task, the next occurrence gets the same advance reminders.
- If you set an advance reminder by mistake, press Ctrl+Z to undo it.
- Changing advance reminders (even clicking Save without changing anything) **doesn't clear earlier acknowledgements**, so tasks you already acknowledged won't pop up again. Acknowledgements are cleared when you edit the due date, or when you change a `Done` task back to not done. Reminders are then recalculated from that moment: advance reminders that have already passed don't pop up, but if the due time has passed, you get the Due now reminder.

**How repeating tasks work**: when you change the status to `Done`, either from the status list or with the button in the reminder window, the original row is kept as history and TableNote inserts a row for the next occurrence below it, with the status `Not started`.
- `Monthly`: if the next month doesn't have that day, the last day of the month is used, then the original day comes back in months that have it. For example, Jan 31 → Feb 28 → Mar 31.
- If the due date has only a date and no time, the next occurrence also has only a date.
- When you change the repeat setting, the 🔁 appears or disappears right away, and Ctrl+Z can undo it.
- The repeat setting is saved in your file as a code, like the status. When a file is opened, both the old Thai values and the English labels are converted to codes, so switching the language doesn't change it.
- Pasting from Excel doesn't create next occurrences.
- If the table is already full (1,000 rows), the next occurrence can't be created and a message tells you so.

**Pasting dates from Excel**: TableNote reads many formats, such as `2026-11-20 14:30`, `20/11/2026 14:30`, `5/1/2026 9:00` or `2026-11-20`. It always reads them as **day/month/year** (`5/1/2026 9:00` is 5 January 2026 at 09:00), whatever language or calendar your computer uses. Single-digit hours and seconds are fine. With no time, 00:00 is used. If you enter a Buddhist Era year, such as `09/11/2569`, TableNote converts it to 2026 for you.

When you change the due date with the calendar, the time box or the 📅 button, that task's snoozes and acknowledgements are cancelled, and reminder times (both advance and on-time) are recalculated immediately.

The **Sort by due** button puts the tasks closest to their due time first. Tasks with no due date go to the bottom of the table.

## 4. What to do when a reminder appears

TableNote checks tasks about every 30 seconds, so a reminder appears within roughly half a minute of its set time. Tasks marked `Done` don't remind you.

When the time comes, you get the **Windows notification sound**, a toast notification in the corner of the screen (silent, so the sound doesn't play twice), and a yellow window on top of everything. This window **doesn't take focus**, so you can keep typing in whatever you're doing. If several tasks are due at the same time, they are listed together in one window. From 2.3.0 there is only ever one reminder window: reminders that come in while it is open are added to its list instead of opening a new window.

Each task shows what kind of reminder it is:
- **⏰ Due in 15 minutes** (or 1 hour, 1 day, and so on) is an advance reminder.
  - If it pops up late, for example because you just opened TableNote, it shows the real time left, such as **Due in 7 minutes (…) — missed at …**.
  - If you leave an advance reminder window open until the due time, the text changes to **Due now** and the sound plays once more. The buttons then apply to the on-time reminder.
- **🔔 Due now** is the on-time reminder. If it is very late, it adds "— missed at ...".
- If one task has several reminders due at the same time, it appears only once, using the time closest to the due time.

Each task in the reminder window has its own buttons:

| Button | What it does |
|---|---|
| **Snooze 10 min** | Snoozes only that reminder for that task; it reminds you again in 10 minutes. TableNote remembers the snooze even if you quit and reopen it. If you snooze an advance reminder past the due time, it merges into the on-time reminder. |
| **Done** | Sets the task to `Done` and stops reminding. If the task repeats, a row for the next occurrence is created. |
| **Acknowledge** | Won't remind again for this reminder time (and any earlier advance reminders), for that task only. If an on-time reminder is still to come, you'll still get it at the due time. |

At the bottom of the window are buttons that apply to every task still in the window. They **work differently**, so choose carefully:

| Button | What it does |
|---|---|
| **Close (acknowledge all)** (with a single task it reads **Close (acknowledge — don't remind again for this time)**) | **Acknowledges** every task still in the window, the same as clicking Acknowledge on each one. |
| **X** at the top-right of the window | **Snoozes 10 minutes**. Tasks still in the window remind you again in 10 minutes. |

> If you haven't done the task yet and want another reminder, click **X** or **Snooze 10 min**. Don't click **Close** or **Acknowledge** on an on-time reminder, because it won't remind you again until you change the due date.
>
> If you change a task to `Done` in the table while the reminder window is open, it disappears from the reminder window within a few seconds.

To turn off the sound, tick **Mute reminders** on the toolbar. The reminder window still appears as usual.

If Windows notifications are turned off (for example, in Focus mode), the toast may not appear, but the yellow reminder window always does.

## 5. What the colors mean

| Row color | Meaning |
|---|---|
| **Light red** | Overdue |
| **Dark yellow** | Due today, but the time hasn't come yet |
| **Pale gray** with strikethrough and faded text | Done |
| Normal light yellow | Not due yet, or no due date |

You can also give a row its own colour or a highlight: right-click the **row number** and choose a colour (8 pastel colours, or none), **Highlight**, or **Clear**. These follow the row when you move or sort it. Due-date colours still apply when a row has no custom colour.

Tick **Hide done** to hide rows that are done, and untick it to show them again.

## 6. Pasting from Excel and copying out

- **Paste from Excel**: copy a range from Excel or Google Sheets, click the cell where pasting should start, then press Ctrl+V. The data spreads across cells for you. Up to 256 KB can be pasted at a time. Pasted data is plain text, and formulas are not calculated.
- **Copy table** copies every cell without the column headers.
- **Copy with headers** copies the whole table with the headers as the first row.
- **Ctrl+C** copies only the selected cells. Shift+click to select several cells.
- Copied data is plain text, ready to paste into Excel.

## 7. Undo

Press **Ctrl+Z** to undo, and **Ctrl+Y** or **Ctrl+Shift+Z** to redo. Up to 100 steps are kept.

Each of these is undone one step at a time:
- Typing in a cell (one step per edit, counted when you press Enter or click out of the cell)
- Editing a column header
- Pasting from Excel (the whole paste is one step)
- Adding or deleting rows and columns (F5 to F8), including delete-column from the header menu
- Moving a column or a row
- Column settings, and a row's colour or highlight
- Changing the status, including clicking **Done** when it creates the next occurrence (one step)
- Setting the due date, repeat, and advance reminders

**While you are typing in a cell**, Ctrl+Z first undoes characters in that cell. After you leave the cell, Ctrl+Z undoes the whole edit.

Undo is tied to the row itself, not its position, so it stays correct even after you click **Sort by due**.

**Not undoable**: snoozes, acknowledgements, hiding done tasks, muting, and the window position.

## 8. Window modes and Pin to desktop

- **Always on top** keeps the window above all others.
- **Lock position** stops the window from being dragged. Unlock it first to move it.
- **Pin to desktop** (Windows only) attaches the table to the desktop. It is display-only, clicks pass through it, and Win+D doesn't hide it.

**4 ways to get back to editing from Pin to desktop mode**
1. Right-click the **TableNote** tray icon and choose **Edit table**.
2. Press **Ctrl+Shift+E**. If another program already uses this shortcut, a message tells you.
3. Launch TableNote again.
4. Run `TableNote.exe --edit`.

The tray menu has **Edit table**, **Help**, **Log**, **Move to center** and **Exit**. If the window ends up off-screen, choose **Move to center**. Closing the main window minimizes it to the tray; the only way to really quit is **Exit**.

## 9. Keyboard shortcuts

| Key | What it does |
|---|---|
| F5 | Add a row |
| F6 | Delete a row |
| F7 | Add a column |
| F8 | Delete a column |
| Alt+Shift+← / → | Move the active column left / right |
| Alt+Shift+↑ / ↓ | Move the active row up / down (skips hidden rows when Hide done is on) |
| Enter | Move to the cell below |
| Shift+Enter | Move to the cell above |
| Ctrl+C | Copy the selected cells, without headers |
| Ctrl+V | Paste text or a table from Excel |
| Ctrl+Z | Undo (see section 7) |
| Ctrl+Y or Ctrl+Shift+Z | Redo |
| Shift+click | Select several cells |
| Shift+click 📅 | Set the due time to tomorrow at 09:00 |
| Ctrl+Shift+E | Back to editing from Pin to desktop mode (Windows) |
| Ctrl++ / Ctrl+− / Ctrl+0 | Zoom in / zoom out / back to 100% (see section 12) |
| Ctrl+mouse wheel | Zoom in or out (see section 12) |
| Ctrl+Home / Ctrl+End | Go to the first cell / the last cell of the last row |

## 10. FAQ

**Where is my data stored?**
- On Windows: `%APPDATA%\TableNote\note.json`. To open the folder, type `%APPDATA%\TableNote` in the File Explorer address bar.
- On Linux: `~/.local/share/TableNote/note.json`.
- Hover over the status bar at the bottom of the window to see the actual file path.

**Can I use a file from an older version?**
Yes. TableNote adds the Status and Due columns for you, and your existing data is kept. Tasks from an old file get on-time reminders only, until you add ⏰ reminders. Before the first save in a newer format, TableNote backs up the original file in the same folder, named after the file's version: `note.v2.bak` for 1.x/2.0, `note.v3.bak` for 2.1.0, and `note.v4.bak` for 2.2–2.3 files (format version 4). Existing backups are never overwritten; if the name is taken, a date and time is added, for example `note.v3.20261109141500.bak`. A 2.3.x build can still open a 2.4.0 (v5) file: it does not reject it or drop the new fields, but it will not show column settings or row colours.


**If the reminder settings in my file are wrong, will TableNote delete my table?**
No. If a value in `reminders` has the wrong type (for example, `"offsets": ["15"]`), TableNote drops only that value, keeps the rest of the table, and records it in `tablenote.log` next to `note.json`.

**It says "The save file couldn't be read" and the table is empty. What do I do?**
Your data is still there. TableNote moved the original file to a `.bak` copy in the same folder.

**It says note.json was larger than 8 MB. What happens?**
Your data is still there. If TableNote opened a file larger than 8 MB (raised from 2 MB in 2.3.0), it first copied the full file to `note.overlimit-<time>.bak` in the same folder and opened the table within the limits. If a save would make `note.json` larger than 8 MB, the last saved file is left unchanged, the current table is copied to `note.overlimit-<time>.bak`, and a message tells you so. Remove some text to save again.

**It says the table is full, or the file has more rows than the limit. What happens?**
- When the table reaches 1,000 rows, you can't add more rows (including next occurrences of repeating tasks) and a message tells you so. Delete or move old rows to make room.
- If a file has more rows than the limit, TableNote first copies the full file to `note.overlimit-<time>.bak` in the same folder (for example `note.overlimit-20261109153000.bak`; never deleted automatically), then opens the first 1,000 rows and shows a message.

**How do I restore from a .bak file?**
1. Right-click the tray icon and choose **Exit** to close TableNote completely.
2. Open the `%APPDATA%\TableNote` folder.
3. Find `note.json.bak`, a file with a date and time in its name such as `note.json.20261109153000.bak`, `note.v2.bak` (data from 1.x/2.0), `note.v3.bak` (data from 2.1.0) or `note.v4.bak` (data from 2.2–2.3), and pick the one you want.
4. Rename the current `note.json` to something else to keep it, for example `note-new.json`.
5. Copy the `.bak` file you picked and rename the copy to `note.json`.
6. Open TableNote again. If it still says the file can't be read, that file is really damaged; try another .bak file.

**Are backup files kept forever?**
Not all of them. From 2.3.0, TableNote keeps only the 5 newest `note.json*.bak` files (backups made when the save file couldn't be read or was too big). When a new one is made, the oldest is deleted. If you want to keep one of these, copy it to another folder.

Version backups (`note.v2.bak`, `note.v3.bak` and so on) and the full backup made when a file has more rows than the limit (`note.overlimit-<time>.bak`) are never deleted automatically and don't count toward the 5.

If two time-stamped backups would get the same name, TableNote adds `-1`, `-2` and so on to the end of the new file's name, so an existing backup is never overwritten.

**Why can't it save?**
The status bar shows "Couldn't save" with the reason, for example the file is locked by another program. Close any program that might have the file open, then edit any cell again so TableNote saves again.

**I deleted something by mistake. Can I undo it?**
Yes. Deleting a row (F6), deleting a column (F8) and clearing text in a cell can all be undone with Ctrl+Z (up to 100 steps; undo history is lost when TableNote quits).

**Why doesn't it remind me again after I click "Close"?**
**Close** and **Acknowledge** both mean you've seen it, so that reminder time won't remind you again. To get another reminder, change the due date. Next time, if you're not ready yet, click **X** or **Snooze 10 min** instead.

**Why does it still remind me when the status says "ยังไม่เสร็จ" (not done)?**
That's expected: that phrase means not done (from 2.3.0 it is converted to the not-done status when the file is opened). When the task is finished, choose `Done` from the list.

**Where can I see the version?**
Click the **?** button on the toolbar, or right-click the tray icon and choose **Help** or **Log**. The version is shown at the top, for example TableNote 2.3.0.

**Why is TableNote still running after I close the window?**
That's by design, so reminders keep working. TableNote stays in the tray. To really quit, right-click the tray icon and choose **Exit**.

**I switched the language or zoom and it's back to English / 100%. Why?**
Language and zoom are saved in `settings.json` next to `note.json` (on Windows, `%APPDATA%\TableNote\settings.json`). If that file is damaged or missing, TableNote goes back to English and 100% zoom. Your tasks in `note.json` aren't affected; just choose the language and zoom again.

**Can I export the table to a txt or Excel file?**
Yes: use **Export** and choose .txt, .csv or .xlsx (see section 13). You can also use **Copy with headers** and paste into Excel.

## 11. Changing the language

- The interface is in **English** by default for everyone, including people upgrading from an older version. Once you choose a language, TableNote remembers it.
- To switch, click the **EN/TH** button at the end of the toolbar, or open **Language / ภาษา** in the tray menu and choose **English** or **ไทย**.
- The change applies right away, without restarting TableNote. A reminder window that is already open stays in the old language until you close it.
- The language you choose is saved in `settings.json`, next to `note.json`, so TableNote remembers it next time. Switching the language doesn't change `note.json`.
- Changing the language changes only the interface text. Your table data, statuses and repeat settings don't change: statuses and repeat settings are stored as codes and only their labels are translated (see section 2). Text you typed yourself, including column headers you renamed, stays as you typed it.
- If `settings.json` is damaged or missing, TableNote goes back to English.
- Both languages show and save years in the Western calendar (CE, for example 2026). You can still type a Thai Buddhist-calendar year in the Due column: any year from 2400 up is treated as Buddhist and 543 is subtracted (for example, 2569 becomes 2026), in either language.

## 12. Resizing the display (zoom)

- Use the **A−**, **100%** and **A+** buttons on the toolbar (next to the language button) or in the tray menu.
- Or press **Ctrl++** to zoom in, **Ctrl+−** to zoom out, and **Ctrl+0** to go back to 100%. You can also hold **Ctrl** and turn the mouse wheel.
- The zoom goes from 80% to 200% in steps of 10%. The status bar shows the current size each time you change it.
- Zoom doesn't work while the table is pinned to the desktop.
- The zoom level is saved in `settings.json`, next to `note.json`, so TableNote remembers it next time. If `settings.json` is damaged or missing, the zoom goes back to 100%.

## 13. Exporting the table

Click **Export** on the toolbar and choose a format:

| Format | What you get |
|---|---|
| **Text (.txt)** | Plain text with columns separated by tabs. Tabs and line breaks inside a cell become a space. |
| **CSV (.csv)** | Comma-separated values, for Excel, Google Sheets and other programs. |
| **Excel (.xlsx)** | An Excel workbook you can open directly in Excel. |

- All three formats support Thai text.
- **.xlsx**: a cell is saved as a number only if Excel would show it back exactly as you typed it, character for character, for example `12`, `-5` or `3.25`. Values such as `+3.5`, `12.50`, `-0`, `007`, `1e5` and numbers with more than 11 digits before the point, such as a 13-digit Thai ID (Excel would show `1.10370E+12`), are saved as text, so they never change. All other cells are saved as text, with no formulas.
- **.csv and .txt**: a cell that starts with `=`, `+`, `-`, `@`, a tab or a line break gets a `'` in front, so Excel doesn't treat it as a formula. Plain numbers such as `-5` or `+3.5` stay as numbers.
- Status labels and repeat settings in the exported file follow the language currently selected, for example `Done` and `Weekly` in English, or `เสร็จแล้ว` and `ทุกสัปดาห์` in Thai. A status you typed yourself is exported as typed.
- An empty status is exported as `Not started` in English or `ยังไม่เริ่ม` in Thai, the same as it appears on screen.
- When **Hide done** is ticked, only the rows you can see are exported. To export the done tasks too, tick **Include completed tasks** in the export window.
- Due dates are written as text in the format `yyyy-MM-dd`, or `yyyy-MM-dd HH:mm` when the task has a time, with a Western (CE) year.
- The first row of the file is the column headers, followed by your rows in the order shown on screen.
- The suggested file name is `TableNote-YYYY-MM-DD`. You choose the folder in the save dialog. The default format in the dialog is Excel (.xlsx). Export works only while you are editing the table, not while it is pinned to the desktop.


## 14. Moving columns and rows, column settings, row colours

**Columns**
- Drag the **⋮⋮** grip on the left of a column header to move that column, or right-click the header and choose Move left / right / to first / to last, or press **Alt+Shift+←** / **Alt+Shift+→**.
- Right-click the header and choose **Delete this column…** to remove it. TableNote always asks for confirmation. The Due column cannot be deleted.
- Right-click the header and choose **Column settings…** to change the name, width, and type:
  - **Text** (default), **Number**, **Choice** (a drop-down; type one option per line), or **Check** (✓).
  - Changing the type does not change the text already in the cells.
  - Status and Due columns can only change name and width.

**Rows**
- Drag the **row number** to move the row, or right-click it and choose Move up / down / to top / to bottom, or press **Alt+Shift+↑** / **Alt+Shift+↓**. When **Hide done** is on, these moves skip hidden rows.
- Right-click the row number to set a colour (8 colours, or none), toggle **Highlight**, or **Clear** the style. The style stays with the row when you move or sort it, and is saved immediately.

Moving columns or rows, deleting a column, changing column settings, and changing a row's colour or highlight can all be undone with **Ctrl+Z**.


---
*This guide is based on the TableNote 2.4.0 code as of 11 Nov 2026 (Thailand time, UTC+7).*
