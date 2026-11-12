namespace TableNote;

public static class AppInfo
{
    public const string Version = "2.4.2";
    public const string ConfirmedCommit = "rewrite-" + Version;
    public const string BuildNote = "วันบิลด์: 12 พฤศจิกายน 2026 (เวลาไทย UTC+7) — แก้บั๊กข้อมูลหาย การบันทึก การส่งออก และข้อความภาษาอังกฤษ";

    public static string HelpText => Localizer.Current == "th" ? HelpTextTh : HelpTextEn;
    public static string LogText => Localizer.Current == "th" ? LogTextTh : LogTextEn;

    public static string HelpTextEn { get; } = (@"TableNote {VERSION}

Shortcuts
  F5 / F6        add / delete row        F7 / F8   add / delete column
  Alt+Shift+←/→  move column left/right  Alt+Shift+↑/↓  move row up/down
  Enter          next cell down          Shift+Enter  cell above
  Ctrl+C / Ctrl+V  copy cells / paste from Excel (plain text, formulas are not calculated)
  Ctrl+Z         undo (up to 100 steps)  Ctrl+Y / Ctrl+Shift+Z  redo
  Ctrl+Home      first cell              Ctrl+End  last row
  Ctrl + / Ctrl − / Ctrl 0   larger / smaller / 100% (80–200%); Ctrl+mouse wheel too; A− 100% A+ buttons
  EN/TH button    switch language (saved in settings.json; note.json is not touched)
  Ctrl+Shift+E   back to editing when pinned to desktop (Windows)

Columns and rows
  Right-click a header → Column settings…: name, width, type (text / number / choice / checkbox).
  Right-click a row number → Row color / Highlight row. A row color replaces the status color;
  a highlighted row gets an orange row number.

Taskbar and tray
  Editing: icon on the taskbar and in the tray. Pinned to desktop: tray icon only.
  Tray → Edit table, Ctrl+Shift+E or unpinning brings the taskbar icon back; the mode is kept after restart.

Reminders
  Set the Due column with the calendar + HH:mm (24-hour). Dates are always CE (e.g. 2026).
    Typed text: yyyy-MM-dd HH:mm or d/M/yyyy, e.g. 5/1/2026 9:00 = 5 Jan 2026 09:00.
    📅 on an empty cell sets now + 1 minute; Shift+click 📅 = tomorrow 09:00.
  Status: Not started / In progress / Done (stored as todo / doing / done). A blank status shows as Not started.
  Repeat: None / Daily / Weekly / Monthly — marking it Done keeps the row as history and adds the next one below.
  ⏰ advance reminders: 15 min / 1 hour / 1 day or custom, up to 5 per task. The on-time reminder always fires.
  Reminders gather in ONE window: Snooze 10 min / Done / Acknowledge; Close = acknowledge all; X = snooze.
  Reminders work while the program runs (closing the window keeps it in the tray; quit from the tray menu).

Export (Export… button or tray menu)
  .xlsx / .csv (UTF-8 with BOM) / .txt (tab-separated; tabs and line breaks inside a cell become a space).
  Status and repeat use the current language's labels; dates are yyyy-MM-dd [HH:mm] (CE).
  In .csv/.txt a value starting with = + - @ gets a leading ' (never a formula), except plain numbers.
  When done rows are hidden you can choose to include them.

Files
  Windows: %APPDATA%\TableNote\note.json   Linux: ~/.local/share/TableNote/note.json
  settings.json (language, zoom) sits next to note.json. tablenote.log keeps 2 files, ≤ 512 KB.
  Older files are backed up once per version (note.v2.bak, note.v3.bak, note.v4.bak …, never overwritten).
  More than 1000 rows: the full file is copied to note.overlimit-<time>.bak (never deleted) and the first 1000 rows open.
  Broken files are kept as note.json*.bak (newest 5).
  Limits: 1000 rows, 30 user columns, 2,000 characters per cell, note.json up to 8 MB.
").Replace("{VERSION}", Version);

    public static string HelpTextTh { get; } = (@"ตารางโน้ต รุ่น {VERSION}
หน้าต่างวิธีใช้นี้ไม่ใช่โหมดลอยของตาราง

วิธีใช้ ตารางโน้ต (TableNote) รุ่น {VERSION}

ปุ่มลัด
  F5             เพิ่มแถว
  F6             ลบแถว
  F7             เพิ่มคอลัมน์
  F8             ลบคอลัมน์
  Alt+Shift+←/→  ย้ายคอลัมน์ซ้าย/ขวา
  Alt+Shift+↑/↓  ย้ายแถวขึ้น/ลง (ข้ามแถวที่ซ่อนเมื่อติ๊กซ่อนงานเสร็จ)
  Enter          ไปช่องถัดลงไป
  Shift+Enter    ไปช่องข้างบน
  Ctrl+C         คัดลอกช่องที่เลือก (ไม่มีหัวคอลัมน์)
  Ctrl+V         วางจาก Excel (ข้อความล้วน ไม่คำนวณสูตร)
  Ctrl+Z         ย้อนการแก้ตาราง (สูงสุด 100 ขั้น)
                 ระหว่างพิมพ์ในช่อง Ctrl+Z ย้อนตัวอักษรในช่องก่อน
  Ctrl+Y / Ctrl+Shift+Z  ทำซ้ำ
  Ctrl+Shift+E   กลับมาหน้าแก้ไขจากโหมดวางแนบพื้นหลัง (Windows)
  Ctrl+Home      ไปช่องแรกของตาราง   Ctrl+End  ไปแถวสุดท้าย
  Ctrl + / Ctrl − / Ctrl 0   ขยาย / ย่อ / กลับ 100% (80–200%)
  Ctrl+ล้อเมาส์   ขยาย/ย่อ   ปุ่ม A− 100% A+ บนแถบเครื่องมือ
  ปุ่ม EN/TH      สลับภาษา (จำไว้ใน settings.json ไม่แตะ note.json)

ตั้งเตือนงาน
  1. พิมพ์งานในคอลัมน์ ""หัวข้อ"" และ ""รายละเอียด""
  2. ตั้ง ""กำหนดส่ง"" ด้วยปฏิทิน + ช่องเวลา HH:mm
     รองรับข้อความ yyyy-MM-dd HH:mm และ วัน/เดือน/ปี เช่น 5/1/2026 9:00 = 5 ม.ค. 2026 09:00
     แสดงและเก็บเป็นปี ค.ศ. ทั้งสองภาษา (ถ้าพิมพ์ปี พ.ศ. เช่น 09/11/2569 จะแปลงเป็น ค.ศ. 2026)
     ปุ่ม 📅 เปิดปฏิทิน (ถ้าช่องว่างจะตั้ง +1 นาทีเพื่อทดสอบ)
     Shift+คลิก 📅 = พรุ่งนี้ 09:00
  3. เลือกสถานะ: ยังไม่เริ่ม / กำลังทำ / เสร็จแล้ว (ในไฟล์เก็บเป็นรหัส todo / doing / done)
     ช่องสถานะว่างแสดงเป็น ยังไม่เริ่ม และยังว่างในไฟล์
  4. ทำซ้ำ: ไม่ซ้ำ / ทุกวัน / ทุกสัปดาห์ / ทุกเดือน
     เมื่อกดเสร็จ แถวเดิมเป็นประวัติ และแทรกแถวรอบถัดไปด้านล่าง
     ทุกเดือนวันที่ 31 → เดือนที่สั้นกว่าใช้วันสุดท้าย แล้วกลับเป็น 31 (31 ม.ค. → 28 ก.พ. → 31 มี.ค.)
  5. ปุ่ม ⏰ เตือนล่วงหน้า: ก่อน 15 นาที / 1 ชั่วโมง / 1 วัน หรือกำหนดเอง
     (นาที/ชั่วโมง/วัน ไม่เกิน 30 วัน) ได้สูงสุด 5 ครั้งต่องาน ค่าซ้ำนับครั้งเดียว
     เตือนตรงเวลายังมีเสมอ  เวลาเตือนที่เลยไปแล้วตอนตั้งค่าจะข้าม (ไม่เด้งทันที)
     งานที่เลยกำหนดไปแล้ว เตือนเฉพาะ ""ครบกำหนดแล้ว"" ไม่เด้งเตือนล่วงหน้าที่พลาดไป
     ⚠ หน้าช่องกำหนดส่ง = อ่านวันที่ไม่ได้ งานนั้นจะไม่เตือน

  เมื่อถึงเวลา จะเด้งหน้าต่างเตือน (รวมหลายงานในหน้าต่างเดียว) พร้อมเสียง
    ""อีก 15 นาทีครบกำหนด"" = เตือนล่วงหน้า   ""ครบกำหนดแล้ว"" = เตือนตรงเวลา
    เลื่อน 10 นาที   เตือนอีกครั้งในอีก 10 นาที (เฉพาะเวลาเตือนนั้น ถ้าเลยกำหนดส่งจะรวมกับเตือนตรงเวลา)
    เสร็จแล้ว        เปลี่ยนสถานะ และสร้างรอบถัดไปถ้าตั้งทำซ้ำไว้
    รับทราบ         ไม่เตือนซ้ำสำหรับเวลานี้ เฉพาะงานนั้น (รวมเวลาเตือนก่อนหน้า)
    ปิด             รับทราบทุกงานที่ยังค้างในหน้าต่าง (แก้กำหนดส่งแล้วจะเตือนใหม่ได้)
    X (ปิดหน้าต่าง) เลื่อน 10 นาที สำหรับงานที่ยังค้าง
  ติ๊ก ""ปิดเสียงเตือน"" ถ้าไม่ต้องการเสียง (ป๊อปอัปยังขึ้น)
  สำคัญ: การเตือนทำงานเมื่อโปรแกรมทำงานอยู่
    ปิดหน้าต่าง (X) = ย่อไปอยู่ที่ถาดไอคอน การเตือนยังทำงาน  ออกจริง: คลิกขวาไอคอนถาด → ออก
    ""เปิดเมื่อเข้าสู่ระบบ"" จะเริ่มแบบซ่อนอยู่ในถาด (--startup)
  สถานะที่ถือว่าเสร็จ: เสร็จ, เสร็จแล้ว, done, complete, completed, ✓, ✔ (ตรงทั้งคำ)
    ""ยังไม่เสร็จ"" / ""ไม่เสร็จ"" ไม่ถือว่าเสร็จ

ความหมายของสีแถว
  แดงอ่อน    เลยกำหนดส่งแล้ว
  เหลือง     ครบกำหนดภายในวันนี้
  เทาจาง + ขีดฆ่า   เสร็จแล้ว
  ปุ่ม ""เรียงตามกำหนด"" / ช่อง ""ซ่อนงานเสร็จ""
  สีแถวที่ตั้งเอง (คลิกขวาที่เลขแถว → สีแถว) ใช้แทนสีพื้นข้างบน  ไฮไลต์แถว = เลขแถวสีส้ม
  คลิกขวาที่หัวคอลัมน์ → ตั้งค่าคอลัมน์… เปลี่ยนชื่อ ความกว้าง และชนิด (ข้อความ / ตัวเลข / ตัวเลือก / ช่องติ๊ก)

ไอคอนแถบงานและถาด
  โหมดแก้ไข: มีไอคอนทั้งที่แถบงานและถาด   โหมดวางแนบพื้นหลัง: มีไอคอนที่ถาดอย่างเดียว
  ถาด → แก้ไขตาราง, Ctrl+Shift+E หรือเลิกแนบพื้นหลัง ไอคอนกลับมาที่แถบงาน  ปิดแล้วเปิดใหม่ได้โหมดเดิม

ที่เก็บไฟล์
  Windows: %APPDATA%\TableNote\note.json
  Linux:   ~/.local/share/TableNote/note.json
  บันทึกอัตโนมัติหลังหยุดพิมพ์ประมาณ 1.5 วินาที
  ก่อนบันทึกเป็นรูปแบบใหม่ครั้งแรก จะสำรองไฟล์เดิมตามรุ่นของไฟล์
    note.v2.bak (รุ่น 1.x/2.0), note.v3.bak (รุ่น 2.1.0), note.v4.bak (รุ่น 2.2–2.3)
    ไม่ทับไฟล์สำรองเดิม ถ้าชื่อซ้ำจะเติมวันเวลาต่อท้าย
  ค่าในไฟล์ที่อ่านไม่ได้จะใช้ค่าเริ่มต้นแทน ตารางยังอยู่ครบ รายละเอียดใน tablenote.log
  ไฟล์ที่มีเกิน 1000 แถว: สำรองไฟล์เต็มเป็น note.overlimit-<วันเวลา>.bak (ไม่ลบเอง) แล้วเปิด 1000 แถวแรก
  ไฟล์เสียเก็บเป็น note.json*.bak ล่าสุด 5 ไฟล์  ภาษา/ขนาดเก็บใน settings.json
  tablenote.log เก็บ 2 ไฟล์ (tablenote.log + tablenote.1.log) รวมไม่เกิน 512 KB
  ขีดจำกัด: ไม่เกิน 1000 แถว 30 คอลัมน์ผู้ใช้ ช่องละ 2,000 ตัวอักษร ไฟล์ note.json ไม่เกิน 8 MB

ส่งออก (ปุ่ม ส่งออก… หรือเมนูถาด)
  .xlsx (Excel) / .csv (UTF-8 มี BOM) / .txt (คั่นด้วยแท็บ — แท็บ/ขึ้นบรรทัดในช่องเป็นช่องว่าง)
  สถานะและทำซ้ำเป็นป้ายตามภาษาที่เลือก  วันที่เป็น yyyy-MM-dd [HH:mm] ปี ค.ศ.
  ค่าที่ขึ้นต้นด้วย = + - @ ใน .csv/.txt มี ' นำหน้า (ไม่ให้เป็นสูตร) ยกเว้นตัวเลขล้วน
  ถ้าซ่อนงานเสร็จอยู่ จะถามว่าจะรวมแถวที่ซ่อนหรือไม่
  ลบคอลัมน์กำหนดส่งไม่ได้ (การแจ้งเตือนใช้คอลัมน์นี้)
").Replace("{VERSION}", Version);

    public static string LogTextEn { get; } =
@"TableNote " + Version + @" — built 12 Nov 2026 (Bangkok time, UTC+7)

12 Nov 2026 — 2.4.2
New: English is the primary language; UI text comes from lang/en.json and lang/th.json, and other languages can be added as JSON files.
New: dates in exports are yyyy-MM-dd / yyyy-MM-dd HH:mm; the log is English only.
New: taskbar icon while editing, tray icon only when attached to the desktop background; the mode is kept after restart.
Fixed: TableNote could close when several reminders came due at once; errors are now logged to tablenote.log instead.

12 Nov 2026 — 2.4.1
Fixed: a note.json saved as Thai ANSI (Windows-874) no longer turns Thai text into ""�"" and overwrites the file; it is read correctly, backed up as note.encoding-<time>.bak and saved back as UTF-8.
Fixed: if note.json is locked at start-up (OneDrive, antivirus), TableNote retries, saves to note.unsaved.json instead of silently discarding the session, and warns on quit when nothing could be saved.
Fixed: a failed upgrade backup (note.vN.bak) is retried under a timestamped name and is no longer counted as done.
Fixed: column settings are ignored (and logged) when columnIds no longer match the columns after a round trip through 2.3.1.
Fixed: undo/redo saves at once; .xlsx keeps long numbers (12+ digits) as text; quotes pasted from Excel are kept; NaN/Infinity are not numbers; cutting at 2,000 characters never splits an emoji.
Fixed: the 2.4 column menu, column settings dialog, column types and row colours are now in English too; help says autosave is about 1.5 seconds.

11 Nov 2026 — 2.4.0
New: move a column by dragging the ⋮⋮ grip on its header, from the header's right-click menu, or with Alt+Shift+←/→.
The header menu also has a delete-column command that always asks for confirmation and can be undone with Ctrl+Z.
Move a row by dragging its row number, from the row-number menu (Move up / Move down / Move to top / Move to bottom), or with Alt+Shift+↑/↓, undoable with Ctrl+Z (when Hide done is on, moves skip hidden rows).
Column settings from the header right-click menu → ""Column settings…"" (name, width, type: text / number / Choice (drop-down) / Checkbox ✓; choice options one per line; changing the type does not alter cell data; Status and Due columns allow only name and width).
Row colour (8 colours plus none), highlight, and clear from the row-number right-click menu; the style follows the row on move or sort; all of this is undoable with Ctrl+Z and is saved immediately.
Compatibility: file format version 5 adds columnIds, columnSettings, and rowStyles. Older files (v2–v4) open as usual and are backed up as note.vN.bak on the first save. Files from a newer version are not downgraded, and unknown fields are kept. If a 2.3.x build opens a v5 file, it still opens the table (it does not reject the file or drop the new fields — they are stored and written back on save, after a note.v5.bak backup) but it does not use columnIds, column settings, or row colours/highlights.

9 Nov 2026 — 2.3.0
Fixed: the window and the reminder popup could stop responding on Windows 10 (cross-process desktop-attach
calls on the UI thread); every reminder opened a new window; the table built every cell of every row
(200 rows ≈ 1 GB RAM); Ctrl+Home / Ctrl+End. New: English/Thai, zoom 80–200%, export .xlsx/.csv/.txt,
1000 rows (larger files backed up first), UI stall watchdog in tablenote.log.
Compatibility: status is stored as todo/doing/done and repeat as none/daily/weekly/monthly:N — v2.2.1
reads both (repeat codes are the same ones 2.2.x already wrote; status codes count as done/not done correctly).

9 Nov 2026 — 2.2.2 (not released separately; included in 2.3.0)
Popup left open no longer says 'missed'; wrong field types no longer wipe the table; files from newer versions are backed up.";

    public static string LogTextTh { get; } =
@"ตารางโน้ต
รุ่นที่แสดง: " + Version + @"
" + BuildNote + @"

12 พ.ย. 2026 — 2.4.2
ใหม่: ภาษาอังกฤษเป็นภาษาหลัก ข้อความหน้าจอมาจาก lang/en.json และ lang/th.json เพิ่มภาษาอื่นได้ด้วยไฟล์ JSON
ใหม่: วันที่ตอนส่งออกเป็น yyyy-MM-dd / yyyy-MM-dd HH:mm และไฟล์ log เป็นภาษาอังกฤษล้วน
ใหม่: ตอนแก้ไขมีไอคอนบนแถบงาน ตอนแนบพื้นหลังเหลือไอคอนในถาดอย่างเดียว ปิดเปิดใหม่ได้โหมดเดิม
แก้: โปรแกรมอาจปิดไปเองเมื่อหลายงานครบกำหนดพร้อมกัน ตอนนี้ error จะบันทึกลง tablenote.log แทน

12 พ.ย. 2026 — 2.4.1
แก้: note.json ที่บันทึกเป็น ANSI ภาษาไทย (Windows-874) ไม่กลายเป็น ""�"" และไม่ถูกเขียนทับอีก อ่านได้ถูกต้อง สำรองเป็น note.encoding-<วันเวลา>.bak แล้วบันทึกกลับเป็น UTF-8
แก้: ถ้า note.json ถูกล็อกตอนเปิดโปรแกรม (OneDrive/แอนติไวรัส) จะลองอ่านใหม่ และบันทึกลง note.unsaved.json แทนการทิ้งงานทั้งรอบ ถ้าบันทึกไม่ได้จะเตือนตอนออก
แก้: ถ้าสำรองก่อนอัปเกรด (note.vN.bak) ไม่สำเร็จ จะลองชื่อที่มีเวลาต่อท้าย และไม่นับว่าสำรองแล้ว
แก้: ถ้า columnIds ไม่ตรงกับคอลัมน์หลังย้อนไปใช้ 2.3.1 จะไม่ใช้ค่าตั้งคอลัมน์เดิม และเขียนลง log
แก้: Undo/Redo บันทึกทันที, .xlsx เก็บตัวเลข 12 หลักขึ้นไปเป็นข้อความ, วางจาก Excel ไม่ตัดเครื่องหมายคำพูด, NaN/Infinity ไม่ใช่ตัวเลข, ตัด 2,000 ตัวอักษรไม่ผ่าอีโมจิ
แก้: เมนูคอลัมน์ หน้าต่างตั้งค่าคอลัมน์ ชนิดคอลัมน์ และสีแถวของ 2.4 มีภาษาอังกฤษแล้ว หน้าวิธีใช้บอกว่าบันทึกอัตโนมัติประมาณ 1.5 วินาที

11 พ.ย. 2026 — 2.4.0
ใหม่: ย้ายคอลัมน์ได้ด้วยการลากที่จับ ⋮⋮ บนหัวคอลัมน์ คลิกขวาที่หัวคอลัมน์ หรือกด Alt+Shift+←/→
เพิ่มคำสั่งลบคอลัมน์ในเมนูคลิกขวา ซึ่งถามยืนยันทุกครั้ง และย้อนกลับได้ด้วย Ctrl+Z
ย้ายแถวได้ด้วยการลากเลขแถว คลิกขวาที่เลขแถว (เลื่อนขึ้น/เลื่อนลง/ย้ายไปบนสุด/ย้ายไปล่างสุด) หรือกด Alt+Shift+↑/↓ และย้อนกลับได้ด้วย Ctrl+Z (เมื่อติ๊ก ""ซ่อนงานเสร็จ"" การย้ายจะข้ามแถวที่ซ่อน)
ตั้งค่าคอลัมน์จากคลิกขวาที่หัวคอลัมน์ → ""ตั้งค่าคอลัมน์…"" (ชื่อ ความกว้าง ชนิด: ข้อความ / ตัวเลข / ตัวเลือกแบบรายการ / ช่องทำเครื่องหมาย ✓ — ตัวเลือกพิมพ์บรรทัดละหนึ่งรายการ การเปลี่ยนชนิดไม่แก้ข้อมูลในช่อง คอลัมน์สถานะและกำหนดส่งตั้งได้แค่ชื่อกับความกว้าง)
สีแถว (8 สี และไม่มีสี) ไฮไลต์ และล้างค่า จากคลิกขวาที่เลขแถว ตามแถวไปเมื่อย้ายหรือเรียง ย้อนได้ด้วย Ctrl+Z และบันทึกทันที
ความเข้ากันได้: รูปแบบไฟล์รุ่น 5 เพิ่ม columnIds, columnSettings, rowStyles ไฟล์เก่า (v2–v4) เปิดได้ตามปกติ และสำรองเป็น note.vN.bak เมื่อบันทึกครั้งแรก ไฟล์จากรุ่นใหม่กว่าจะไม่ถูกลดรุ่น และฟิลด์ที่ไม่รู้จักถูกเก็บไว้ ถ้ารุ่น 2.3.x เปิดไฟล์ v5 จะเปิดตารางได้ตามปกติ ไม่ปฏิเสธและไม่ลบฟิลด์ใหม่ (เก็บไว้แล้วเขียนกลับเมื่อบันทึก พร้อมสำรอง note.v5.bak) แต่ยังไม่ใช้ columnIds, ตั้งค่าคอลัมน์ และสี/ไฮไลต์แถว

9 พ.ย. 2026 — 2.3.0
แก้: หน้าต่างหลักและป๊อปอัปเตือน ""ไม่ตอบสนอง"" บน Windows 10 (เรียก Win32 ข้ามโปรเซสกับ Explorer บน UI thread),
เตือนแต่ละครั้งเปิดหน้าต่างใหม่ (ตอนนี้รวมในหน้าต่างเดียว), ตารางสร้างทุกช่องทุกแถว (ตอนนี้สร้างเฉพาะแถวที่เห็น),
Ctrl+Home / Ctrl+End ใช้ได้แล้ว
ใหม่: ภาษาอังกฤษ/ไทย, ขยาย/ย่อ 80–200%, ส่งออก .xlsx/.csv/.txt, 1000 แถว (ไฟล์เกินสำรองก่อน),
ตัวเฝ้า UI ค้างเกิน 2 วินาทีบันทึกใน tablenote.log
ความเข้ากันได้: สถานะเก็บเป็น todo/doing/done ทำซ้ำเป็น none/daily/weekly/monthly:N — v2.2.1 อ่านได้
(รหัสทำซ้ำเป็นชุดเดิมที่ 2.2.x เขียนอยู่แล้ว)

9 พ.ย. 2026 — 2.2.2 (รวมอยู่ใน 2.3.0)
แก้: ป๊อปอัปเปิดค้างแล้วขึ้น ""พลาดการเตือน"" ผิด, ค่าผิดชนิดในฟิลด์อื่น (version, columnWidths, hideDone, window, rowIds ฯลฯ)
ทำให้ตารางหาย, ไฟล์จากรุ่นใหม่กว่า (version 5+) ถูกบันทึกทับ — ตอนนี้สำรอง note.v5.bak และเก็บฟิลด์ที่ไม่รู้จัก,
วันที่ 5/1/2026 9:00 อ่านเป็น 5 ม.ค. 2026, ติดพื้นโต๊ะบน Windows 10 ทนขึ้น (WorkerW/Progman, ลองซ้ำ, Explorer รีสตาร์ต)
และมีโหมดสำรองที่ไม่หายเมื่อกด Win+D, บันทึกขั้นที่ล้มเหลวใน tablenote.log

9 พ.ย. 2026 — 2.2.1
แก้: บันทึกหน้าต่าง ⏰ แล้วงานที่รับทราบไว้เด้งซ้ำ, ค่า reminders ผิดชนิดทำให้ทั้งไฟล์ถูกย้ายเป็น .bak,
ข้อความเตือนล่วงหน้าที่เด้งช้าแสดงเวลาที่เหลือจริง + ""พลาดการเตือนเมื่อ"", ป๊อปอัปค้างถึงกำหนดเปลี่ยนเป็นครบกำหนด,
toast เงียบ (ไม่ดัง 2 ครั้ง), สำรองไฟล์ก่อนอัปเกรดตามรุ่น (note.v3.bak) ไม่ทับของเดิม

9 พ.ย. 2026 — 2.2.0
เตือนล่วงหน้า ⏰ (15 นาที/1 ชั่วโมง/1 วัน/กำหนดเอง สูงสุด 5), ปุ่มรับทราบรายงาน,
ปิดหน้าต่าง=ย่อไปถาด, เริ่มตอนเข้าระบบแบบซ่อน, toast Windows (TFM Windows),
แก้: ""ยังไม่เสร็จ"" ถูกนับว่าเสร็จ, Ctrl+Z ในช่องพิมพ์, ย้อนการพิมพ์/หัวคอลัมน์/วาง/F5 F7 F8,
ทุกเดือนวันที่ 31 คงวันตั้งต้น, รอบถัดไปของงานไม่มีเวลาไม่เติม 00:00, ห้ามลบคอลัมน์กำหนดส่ง

9 พ.ย. 2026 — 2.1.0
ปฏิทิน+เวลา, ทำซ้ำรายวัน/สัปดาห์/เดือน, ป๊อปอัปรวม, ปิด=รับทราบ / X=เลื่อน,
ปี พ.ศ., คอลัมน์บทบาทคงที่, toast Windows (ถ้าได้), ขีดฆ่า, Ctrl+Z/Y, ปิดเสียง";
}
