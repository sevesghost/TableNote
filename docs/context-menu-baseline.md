# Context-menu baseline

Baseline of every right-click menu in TableNote at `integration/v2.5.0` commit `b01dd1e` (17 November 2026). This file is an inventory only. It does not change how the menus work.

Labels are the strings in `lang/th.json` and `lang/en.json` at that commit. A checkmark prefix (`✓ `) is added in code when a detail toggle is already on. It is not part of the language string.

Shortcuts in the **Shortcut** column are the `InputGesture` shown on the item. Where the same keys are also handled outside the menu, that is noted. An em dash means the item shows no shortcut.

**Edit mode** means `ViewMode.Edit`. **Background mode** means `ViewMode.Background`.

## How the column and row menus are assembled

Constructor order in `MainWindow.axaml.cs`:

1. `InitSettings` assigns `_extraColumnMenuItems` and `_extraRowMenuItems`.
2. `InitDetailPopup` wraps both delegates and appends the detail items.
3. `InitSizing` wraps both delegates again and appends Column width… / Row height….
4. `InitAlertBoxes` and `InitColors` do not touch either delegate.

`BuildColumnMenu` (`MainWindow.ColumnMenu.cs`) then adds the group block itself, after the wrapped extras. `BuildRowMenu` (`MainWindow.RowMenu.cs`) appends whatever the wrapped row delegate returns, and rebuilds that tail every time the menu opens.

A later change that assigns `_extraColumnMenuItems` or `_extraRowMenuItems` instead of wrapping the previous delegate drops every item registered before that assignment.

## Item counts

Counts are labeled commands, not separators. Group-child names under **Remove child column** are extra and called N below.

| Menu | When | Items |
| --- | --- | --- |
| Column header, name box, plain column | Edit mode, column is not a group parent or child | 13 |
| Column header, ⋮⋮ grip, plain column | Same, right-click the grip | 10 |
| Column header, name box, group parent | The column is a group parent | 17 + N |
| Column header, ⋮⋮ grip, group parent | Same, right-click the grip | 14 + N |
| Column header, name box, group child | The column is a child of a group | 14 |
| Column header, ⋮⋮ grip, group child | Same, right-click the grip | 11 |
| Row header (row number) | Edit mode | 9 on the menu, plus 9 inside **Row color** (18 labeled items) |
| Detail button (**More** / **ดู**) | The cell shows the detail button | 2 |
| Data cell | Any cell in the grid | 0 application items |
| Alert box | The card above the table | 0 context-menu items (buttons, listed below) |
| Tray icon | Right-click the tray icon | 10, plus one radio per language (2 today: English, ไทย) |

The plain-column name-box menu is the one that shows ตัด, คัดลอก, วาง, the four move commands, ตั้งค่าคอลัมน์…, both detail toggles, ความกว้างคอลัมน์…, สร้างกลุ่มที่ยุบ/ขยายได้…, and ลบคอลัมน์นี้….

Outside Edit mode the column header and its grip are not hit-testable (`TextBox.IsHitTestVisible` and the grip are set from the view), and `AttachRowMove` does not attach the row menu. The detail button can still be right-clicked. Its **Hide** item is disabled.

Collapsed child columns are a zero-width placeholder (`HiddenColumnPlaceholder`) with no header and no menu.

## Column header menu

Built by `BuildColumnMenu` in `MainWindow.ColumnMenu.cs`. The name box passes the `TextBox`, so Cut / Copy / Paste are included. The grip passes `null`, so those three are omitted. Both menus are the same object shape otherwise.

The header `TextBox` sets `ContextFlyout = null` before this menu is assigned, so the platform text flyout is not used on headers.

Shared items, in order:

| # | Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `col.cut` | ตัด | Cut | Ctrl+X | `TextBox.Cut` on the header box | Always, when the item exists (name box only). The header itself is not hit-testable in background mode. |
| 2 | `col.copy` | คัดลอก | Copy | Ctrl+C | `TextBox.Copy` | Same as Cut. |
| 3 | `col.paste` | วาง | Paste | Ctrl+V | `TextBox.Paste` | Same as Cut. |
| 4 | `col.move_left` | ย้ายไปทางซ้าย | Move left | Alt+Shift+Left | `RequestMoveColumn(column, column - 1)` → `MoveColumnAsync` → `TableSession.MoveColumn` | Edit mode and the column is not the first. The same keys are handled by `HandleColumnMoveKeys` for the active column. |
| 5 | `col.move_right` | ย้ายไปทางขวา | Move right | Alt+Shift+Right | `RequestMoveColumn(column, column + 1)` | Edit mode and the column is not the last. Also `HandleColumnMoveKeys`. |
| 6 | `col.move_first` | ย้ายไปซ้ายสุด | Move to far left | — | `RequestMoveColumn(column, 0)` | Edit mode and the column is not the first. |
| 7 | `col.move_last` | ย้ายไปขวาสุด | Move to far right | — | `RequestMoveColumn(column, count - 1)` | Edit mode and the column is not the last. |
| 8 | `colset.menu` | ตั้งค่าคอลัมน์… | Column settings… | — | `OpenColumnSettings` → `ColumnSettingsAsync` | Edit mode. Tooltip `colset.type_now` shows the current type. `ColumnSettingsAsync` returns immediately when the view is not Edit. |
| 9 | `detail.column_toggle` | ปุ่มรายละเอียดทุกช่องในคอลัมน์นี้ | Detail button on every cell in this column | — | `ToggleColumnButton` → `CellDetailOps.SetColumnButton` | Edit mode. Header is `✓ ` plus the label when `ColumnButtonOn` is already true. Click turns that flag the other way. |
| 10 | `detail.cell_toggle` | ปุ่มรายละเอียดที่แถวที่เลือก | Detail button on the selected row | — | `ToggleCellButton` for `TableSession.TargetRow` | Edit mode and the table has at least one row. Header is `✓ ` plus the label when that cell already shows the button (`ShowsButton`). |
| 11 | `size.col_menu` | ความกว้างคอลัมน์… | Column width… | — | `OpenColumnWidth` → `ColumnWidthAsync` | Edit mode and `SizeSpec.IsHiddenColumn` is false. A collapsed column stays disabled and shows tooltip `size.hidden_column`. |
| 12 | group block | See the three variants below | | — | | |
| 13 | `col.delete` or `col.delete_due` | ลบคอลัมน์นี้… or ลบคอลัมน์ (คอลัมน์กำหนดส่งลบไม่ได้) | Delete this column… or Delete column (the Due column can't be deleted) | — | `RequestDeleteColumn` → `DeleteColumnAtAsync` → `TableSession.DeleteColumnAtAsync` | Edit mode, more than one column, and the column is not the due column. The due column shows `col.delete_due` and is disabled. |

### Group block on a plain column

One item, in place of the group block.

| Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- |
| `colgroup.make` | สร้างกลุ่มที่ยุบ/ขยายได้… | Make collapsible group… | — | `RequestMakeGroup` → `MakeGroupAsync` → `ColumnGroupOps` create | Edit mode, the column is not Status or Due (`ColumnSettingsOps.IsSystemColumn`), and there is room for at least one more user column (`MaxUserColumns - column count >= 1`). Tooltip is `colgroup.system` when it is Status or Due, otherwise `colgroup.make_tip`. |

Plain name-box total: 13. Plain grip total: 10 (no Cut, Copy, Paste).

### Group block on a group parent

These five replace **Make collapsible group…**. Collapse and Expand are the same slot: only one label is shown.

| Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- |
| `colgroup.collapse` | ยุบกลุ่ม | Collapse group | — | `RequestToggleGroup` → `ToggleGroupAsync` | Edit mode, and the group is currently expanded. |
| `colgroup.expand` | ขยายกลุ่ม | Expand group | — | `RequestToggleGroup` | Edit mode, and the group is currently collapsed. |
| `colgroup.add_child` | เพิ่มคอลัมน์ย่อย | Add child column | — | `RequestAddChild` → `ColumnGroupOps.AddChild` | Edit mode, room for one more user column, and the group has fewer than `ColumnGroupOps.MaxChildCount` (12) children. |
| `colgroup.rename_children` | เปลี่ยนชื่อคอลัมน์ย่อย… | Rename child columns… | — | `RequestRenameChildren` | Edit mode. |
| `colgroup.remove_child` | ลบคอลัมน์ย่อย | Remove child column | — | Submenu only. No click handler on the parent. | Edit mode. |
| `colgroup.remove_named` | ลบ "{child name}"… | Remove "{child name}"… | — | `RequestDeleteColumn` on that child | Edit mode. One item per current child. |
| `colgroup.ungroup` | ยกเลิกกลุ่ม (เก็บคอลัมน์ไว้) | Ungroup (keep columns) | — | `RequestUngroup` → `ColumnGroupOps.Ungroup` | Edit mode. |

Name-box total: 12 shared items other than **Make collapsible group…**, plus these 5, plus N child names = 17 + N. Grip total: 14 + N.

The ▸ / ▾ control on the parent header (`MakeGroupToggle`) is a button, not a menu item. It calls the same `RequestToggleGroup`. It is enabled in Edit mode.

### Group block on a group child

These two replace **Make collapsible group…**.

| Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- |
| `colgroup.child_of` | คอลัมน์ย่อยของ "{parent}" | Child of "{parent}" | — | None | Always disabled. |
| `colgroup.remove_named` | ลบ "{this column}"… | Remove "{this column}"… | — | `RequestDeleteColumn` on this column | Edit mode and the table has more than one column. |

Name-box total: 14. Grip total: 11.

## Row header menu

Built by `BuildRowMenu` in `MainWindow.RowMenu.cs` and attached in `AttachRowMove`. Attached only in Edit mode, on the row-number border. Opening the menu calls `NoteSizingSelection` (so Row height… still sees the previous shift-selection), then `TableSession.ChooseRow`.

Move items are enabled on open when the app is not busy and `RowOps.TargetFor` returns a destination. With **Hide done** on, that destination is among visible rows only. The same Alt+Shift+Up/Down keys are handled by `HandleRowMoveKey` for the active row.

| # | Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `row.move_up` | เลื่อนขึ้น | Move up | Alt+Shift+Up | `MoveRowBy(row, Up)` → `RowOps.MoveRow` | Not busy, and the row can move up among the rows the menu considers. |
| 2 | `row.move_down` | เลื่อนลง | Move down | Alt+Shift+Down | `MoveRowBy(row, Down)` | Not busy, and the row can move down. |
| 3 | `row.move_top` | ย้ายไปบนสุด | Move to top | — | `MoveRowBy(row, Top)` | Not busy, and the row is not already first in that list. |
| 4 | `row.move_bottom` | ย้ายไปล่างสุด | Move to bottom | — | `MoveRowBy(row, Bottom)` | Not busy, and the row is not already last in that list. |
| 5 | `rowstyle.colour` | สีแถว | Row color | — | Opens the submenu below. No action of its own. | Edit mode. |
| 6 | `rowstyle.highlight` | ไฮไลต์แถว | Highlight row | — | `ChangeRowStyle` → `RowStyleOps.SetHighlight` with the opposite of the value captured when the menu was built | Edit mode. Checkbox shows the current highlight. |
| 7 | `rowstyle.clear` | ล้างสีและไฮไลต์ | Clear color and highlight | — | `ChangeRowStyle` → `RowStyleOps.Clear` | Edit mode, and the row has a color or a highlight. |
| 8 | `detail.row_toggle` | ปุ่มรายละเอียดของ "{column}" | Detail button for "{column}" | — | `ToggleCellButton` for this row and the active column (or column 0 when the active column is out of range) | Edit mode. The item is not added when the row id is gone or the table has no columns. `✓ ` is prefixed when that cell already shows the button. |
| 9 | `size.row_menu` | ความสูงแถว… | Row height… | — | `OpenRowHeight` → `RowHeightAsync` | Edit mode. The rows it applies to are the selection saved by `NoteSizingSelection` before this menu selects the clicked row. |

**Row color** submenu, in order. Each entry is a radio. **No color** is checked when the row has no palette color. A palette entry is checked when `RowStyle.Color` equals its key. The click calls `ChangeRowStyle` → `RowStyleOps.SetColor`.

| Id | Thai | English |
| --- | --- | --- |
| `rowstyle.none` | ไม่มีสี | No color |
| `colour.yellow` | เหลือง | Yellow |
| `colour.orange` | ส้ม | Orange |
| `colour.red` | แดง | Red |
| `colour.pink` | ชมพู | Pink |
| `colour.purple` | ม่วง | Purple |
| `colour.blue` | ฟ้า | Blue |
| `colour.green` | เขียว | Green |
| `colour.gray` | เทา | Gray |

Top-level items: 9. Items inside Row color: 9. Labeled items on this menu: 18.

## Detail-button menu

Built in `AttachDetailButton` (`MainWindow.DetailPopup.cs`) on the **More** / **ดู** button. The button is added only when `CellDetailOps.ShowsButton` is true (the column flag is on and the cell is not hidden, or the cell flag is on). Left-click and **Open detail table** both call `OpenDetailPopup`. The button stays enabled in background mode so the table can be opened read-only.

| # | Id | Thai | English | Shortcut | Handler | Enabled |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `detail.open` | เปิดตารางรายละเอียด | Open detail table | — | `OpenDetailPopup` → `OpenDetailAsync` → `CellDetailDialog` | Always. In background mode the dialog is read-only and does not write. |
| 2 | `detail.hide_cell` | ซ่อนปุ่มรายละเอียดของช่องนี้ | Hide the detail button on this cell | — | `ToggleCellButton(row, column, false)` → `CellDetailOps.SetCellButton` | Edit mode. |

The detail dialog's Add row, Delete row, Add column, Delete column, Close, and Cancel controls are buttons (`CellDetailDialog`), not a context menu.

## Data cell

`MakeCell` (`MainWindow.axaml.cs`) does not assign a `ContextMenu` or `ContextFlyout`.

| Cell kind | Right-click |
| --- | --- |
| Text, number | The `TextBox` from `MakeTextBox`. TableNote does not add items. Header boxes are the exception: they clear `ContextFlyout` and use the column menu above. |
| Status, choice | `ComboBox`. No application menu. |
| Check | Check control from `MakeCheckCell`. No application menu. |
| Due | Date, time, repeat, and reminder controls. No application menu. |
| Detail button | The two-item menu in the previous section, on the button only. |
| Collapsed group child | Non-hit-test placeholder. No menu. |

So the application cell context menu has **0** items. Clipboard keys on a focused text cell are the `TextBox` keys (Ctrl+C / Ctrl+X / Ctrl+V), including the grid paste handled in `MakeTextBox`, not a TableNote menu.

## Alert box

`BuildAlertCard` (`MainWindow.AlertBoxes.cs`) does not create a `ContextMenu`. There is nothing to right-click for a menu. The card has buttons, listed here so a later menu bar does not look for items that were never on a menu.

| Control | Thai | English | Shortcut | Handler | Enabled / visible |
| --- | --- | --- | --- | --- | --- |
| Add | + กล่องแจ้งเตือน | + Alert | — | `AddAlert` | Visible in Edit mode (`alert.add`, tooltip `alert.add_tip`). |
| Up | ขึ้น | Up | — | `MoveAlertBy(id, -1)` | Visible in Edit mode. Enabled when the box is not first. Tooltip `alert.move_up`. |
| Down | ลง | Down | — | `MoveAlertBy(id, 1)` | Visible in Edit mode. Enabled when the box is not last. Tooltip `alert.move_down`. |
| Delete | ลบ | Delete | — | `DeleteAlertAsync` | Visible in Edit mode. Always enabled. Tooltip `alert.delete_tip`. |

The ⋮⋮ grip reorders by drag. It is not a menu. Tooltip `alert.drag_tip`.

Context-menu item count: **0**.

## Colors

`InitColors` adds a toolbar button (`color.button`: สี / Colors). It does not add a context-menu item.

## Tray icon menu

Built in `TryCreateTray` (`MainWindow.axaml.cs`). This is a `NativeMenu` on the tray icon, not a control inside the table. Items have no `InputGesture`. They stay enabled. The menu is absent when the tray icon cannot be created.

| # | Id | Thai | English | Shortcut | Handler |
| --- | --- | --- | --- | --- | --- |
| 1 | `tray.edit` | แก้ไขตาราง | Edit table | — | `ShowEditor` |
| 2 | `tray.help` | วิธีใช้ | Help | — | `ShowHelp` |
| 3 | `tray.log` | Log | Change log | — | `ShowLog` |
| 4 | `tray.export` | ส่งออก… | Export… | — | `EnterEdit`, then `ExportAsync` |
| 5 | `tray.language` | Language / ภาษา | Language / ภาษา | — | Submenu. One radio per `Localizer.Supported`, label `_language.name` (English, ไทย). Click calls `SwitchLanguage`. The current language is checked. |
| 6 | `tray.zoom_in` | ขยาย (A+) | Zoom in (A+) | — | `SetZoom` up one step. Ctrl++ and Ctrl+wheel are separate window shortcuts, not gestures on this item. |
| 7 | `tray.zoom_out` | ย่อ (A−) | Zoom out (A−) | — | `SetZoom` down one step. |
| 8 | `tray.zoom_reset` | ขนาด 100% | Reset zoom (100%) | — | `SetZoom(100)` |
| 9 | `tray.center` | ย้ายกลับกลางจอ | Move to center | — | `CenterEditor` |
| 10 | `tray.quit` | ออก | Quit | — | `QuitApp` |

Top-level items: 10. Language radios today: 2.

## Wiring checklist

Use this list when a later branch adds a top menu that is supposed to mirror these commands. Every id below must still be present, with the same handler. Separators are not ids.

Column header, every column:

- `col.cut`, `col.copy`, `col.paste` on the name box only, handlers `Cut` / `Copy` / `Paste`
- `col.move_left`, `col.move_right`, `col.move_first`, `col.move_last` → `RequestMoveColumn`
- `colset.menu` → `OpenColumnSettings`
- `detail.column_toggle` → `ToggleColumnButton`
- `detail.cell_toggle` → `ToggleCellButton`
- `size.col_menu` → `OpenColumnWidth`
- `col.delete` / `col.delete_due` → `RequestDeleteColumn`

Column header, one of these three group shapes, never mixed:

- Plain: `colgroup.make` → `RequestMakeGroup`
- Parent: `colgroup.collapse` or `colgroup.expand` → `RequestToggleGroup`; `colgroup.add_child` → `RequestAddChild`; `colgroup.rename_children` → `RequestRenameChildren`; `colgroup.remove_child` with one `colgroup.remove_named` per child → `RequestDeleteColumn`; `colgroup.ungroup` → `RequestUngroup`
- Child: `colgroup.child_of` (disabled); `colgroup.remove_named` → `RequestDeleteColumn`

Row header:

- `row.move_up`, `row.move_down`, `row.move_top`, `row.move_bottom` → `MoveRowBy`
- `rowstyle.colour` containing `rowstyle.none` and `colour.yellow`, `colour.orange`, `colour.red`, `colour.pink`, `colour.purple`, `colour.blue`, `colour.green`, `colour.gray` → `ChangeRowStyle`
- `rowstyle.highlight`, `rowstyle.clear` → `ChangeRowStyle`
- `detail.row_toggle` → `ToggleCellButton`
- `size.row_menu` → `OpenRowHeight`

Detail button:

- `detail.open` → `OpenDetailPopup`
- `detail.hide_cell` → `ToggleCellButton`

Do not expect application menu ids on a data cell, an alert card, or the Colors button.

### What to re-check by hand

In Edit mode, on a plain column that is not first or last:

1. Right-click the header name. Count 13 items in the order in the column table, including both detail lines and **Make collapsible group…**.
2. Right-click the ⋮⋮ grip. The same list without Cut, Copy, and Paste (10 items).
3. Right-click a row number. Four move lines, then Row color (9 children), Highlight, Clear, one detail line, Row height.
4. Turn the column detail button on, right-click **More** / **ดู**. Two items. **Hide** removes the button and keeps any detail data.
5. Make a group. The parent header shows Collapse or Expand, Add child, Rename, Remove child, and Ungroup instead of Make collapsible group…. A child header shows Child of… and Remove "{name}"….
6. Switch to background mode. The row number has no menu. The header cannot be right-clicked. A detail button that is already showing still opens, and **Hide** is disabled.

### A source check that can be re-run

No UI is required. From the repository root, each id above should still occur as `Localizer.T("that.id"` in the builder file, and the handler name should still occur in that file:

| Ids | File |
| --- | --- |
| `col.cut`, `col.copy`, `col.paste`, `col.move_left`, `col.move_right`, `col.move_first`, `col.move_last`, `col.delete`, `col.delete_due`, `colgroup.*` | `MainWindow.ColumnMenu.cs` |
| `colset.menu`, `rowstyle.colour`, `rowstyle.none`, `rowstyle.highlight`, `rowstyle.clear`, `colour.yellow`, `colour.orange`, `colour.red`, `colour.pink`, `colour.purple`, `colour.blue`, `colour.green`, `colour.gray` | `MainWindow.Settings.cs` |
| `detail.column_toggle`, `detail.cell_toggle`, `detail.row_toggle`, `detail.open`, `detail.hide_cell` | `MainWindow.DetailPopup.cs` |
| `size.col_menu`, `size.row_menu` | `MainWindow.Sizing.cs` |
| `row.move_up`, `row.move_down`, `row.move_top`, `row.move_bottom` | `MainWindow.RowMenu.cs` |
| `tray.edit`, `tray.help`, `tray.log`, `tray.export`, `tray.language`, `tray.zoom_in`, `tray.zoom_out`, `tray.zoom_reset`, `tray.center`, `tray.quit` | `MainWindow.axaml.cs` |

Also confirm the constructor still calls `InitSettings`, then `InitDetailPopup`, then `InitSizing`, and that both popup and sizing methods still copy the previous `_extraColumnMenuItems` and `_extraRowMenuItems` instead of replacing them. `BuildColumnMenu` must still call `BuildColumnGroupMenuItems` after the extra-items block.

That check catches a dropped or unwired command. It does not catch a wrong enabled rule or a wrong label. The hand list above covers those.
