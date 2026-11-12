# Changelog

All notable changes to TableNote. [ภาษาไทย](CHANGELOG.th.md)

- **2.4.2** (12 November 2026)
  - New: English is the primary language. All UI text lives in `lang/en.json` and `lang/th.json`; add a language by adding a JSON file (see CONTRIBUTING.md). Export labels, dates (`yyyy-MM-dd`, `yyyy-MM-dd HH:mm`), and the log are language-independent or English.
  - New: taskbar icon while editing, tray icon only when attached to the desktop background; the mode is kept after restart.
  - Fixed: TableNote could close without warning when several reminders came due at once; reminder popup, toast and sound are now guarded, and any unexpected error is written to `tablenote.log` instead of closing the app.
  - Repo: MIT LICENSE, font OFL, CHANGELOG, CONTRIBUTING; build output no longer tracked.
- **2.4.1** (12 November 2026)
  - Fixed: reading a `note.json` saved as Thai ANSI (Windows-874), locked `note.json` handling, upgrade backups, column-id mismatch, long numbers in xlsx, pasted quotes, English UI wording, undo/redo save, help text.
- **2.4.0** (11 November 2026)
  - New: move columns by dragging the ⋮⋮ header grip, from the header menu, or with Alt+Shift+←/→; delete a column from the header menu (always confirms, undo with Ctrl+Z). Move rows by dragging the row number, from the row-number menu, or with Alt+Shift+↑/↓ (skips hidden rows when Hide done is on; undo with Ctrl+Z). Column settings (name, width, type: text / number / choice / check) from the header menu. Row colour (8 colours plus none) and highlight from the row-number menu; styles follow the row on move or sort and are saved immediately.
  - Compatibility: `note.json` version 5 adds `columnIds`, `columnSettings`, and `rowStyles`. Older files (v2–v4) open as usual and are backed up as `note.vN.bak` on the first save. Newer-version files are not downgraded; unknown fields are kept. A 2.3.x build opens a v5 file without rejecting it or dropping the new fields, but does not use those features.

- **2.3.0** (9 November 2026, includes 2.2.2)
  - Fixed: the main window and reminder popup could go "Not Responding" on Windows 10 (desktop-attach Win32 calls to Explorer ran on the UI thread; now off-thread with SMTO_ABORTIFHUNG 1000 ms, a UI watchdog logs stalls > 2 s to `tablenote.log`); every reminder opened another popup (now one popup that gathers all reminders); the table built every cell of every row (now virtualized, 1000 rows usable); Ctrl+Home / Ctrl+End.
  - New: English/Thai (English default, choice saved in `settings.json`), zoom 80–200% (A− / 100% / A+, Ctrl +/−/0, Ctrl+wheel), Export to .xlsx / .csv / .txt, 1000 rows (bigger files are copied to `note.overlimit-<time>.bak` first), note.json cap 8 MB (not saved over when exceeded), log capped at 512 KB (2 files), 5 rolling `note.json*.bak`.
  - Storage: status saved as `todo`/`doing`/`done`, repeat as `none`/`daily`/`weekly`/`monthly:N`. Dates are CE; Buddhist-era years typed (≥ 2400) are converted.

- **2.2.2** (9 November 2026)
  - Fixed:
    - a popup left open for more than 2 minutes wrongly said "พลาดการเตือนเมื่อ …" (missed at …); lateness is now decided once, when the reminder appears;
    - a wrongly typed field outside `reminders` (for example `"version": "3"`, `"columnWidths": ["120"]`, `"hideDone": "true"`, a bad `window` or `rowIds`) made TableNote discard the whole table; each field now falls back to its default, and a notice points to `tablenote.log`;
    - a file from a newer TableNote (version 5 or higher) was saved over as version 4, losing unknown fields; it is now backed up as `note.v5.bak` first, keeps its version, and unknown fields are preserved;
    - `5/1/2026 9:00` could be misread on a Thai/Buddhist-calendar PC; it now always means 5 January 2026;
    - desktop (background) mode on Windows 10 is more robust: standard WorkerW/Progman attach, retries at startup, re-attach after Explorer restarts, a Win+D-proof fallback, and the failing step is logged.
  - Changed: when an open advance popup reaches the due time, the silent toast is shown too.
- **2.2.1** (9 November 2026)
  - Fixed:
    - saving the ⏰ dialog, even unchanged, made an acknowledged overdue task pop up again;
    - a wrongly typed value in `reminders` made TableNote treat the whole file as corrupt (move it to `.bak`); bad values are now dropped per task and logged;
    - a late advance reminder showed the configured offset instead of the real time left, and an advance popup left open past the due time didn't update;
    - the upgrade backup was skipped when `note.v2.bak` already existed; backups are now named by the old file's version and never overwritten.
  - Changed: the Windows toast is silent so the reminder sound plays only once.
- **2.2.0** (9 November 2026)
  - Added:
    - advance reminders (⏰) with presets or a custom time, up to 5 per task;
    - a per-task Acknowledge button in the popup;
    - closing the window now hides to the tray, and launch at sign-in starts hidden;
    - Windows toast through the Windows target framework;
    - undo for typing, header edits, paste, and F5/F7/F8, keyed by row id.
  - Fixed:
    - `ยังไม่เสร็จ` was counted as done;
    - Ctrl+Z didn't work while typing;
    - the monthly anchor on the 31st was lost;
    - date-only recurrences gained a 00:00 time;
    - the due column could be deleted.
- **2.1.0** (9 November 2026): calendar and time picker; daily, weekly, and monthly recurring tasks; combined reminder popup; Close counts as acknowledged and X snoozes; Buddhist Era years; columns tracked by role so headers can be renamed; strikethrough for done tasks; undo and redo for some actions; mute option.
- **2.0.0**: full rewrite of 1.0.0 with the reminder system, status dropdown, and row colors.
- **1.0.0**: first release.
