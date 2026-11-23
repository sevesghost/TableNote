namespace TableNote;

public static class AppInfo
{
    public const string Version = "2.6.1.1";
    public const string ConfirmedCommit = "rewrite-" + Version;
    public const string BuildNote = "วันบิลด์: 23 พฤศจิกายน 2026 (เวลาไทย UTC+7) — วาดเขียนบนตารางเป็นเลเยอร์ รูปภาพ และภาพในช่อง";

    public static string HelpText => Localizer.Current == "th" ? HelpTextTh : HelpTextEn;
    public static string LogText => Localizer.Current == "th" ? LogTextTh : LogTextEn;

    /// <summary>2.6.0: help in topics ("# Title", "Key :: text", "- bullet"); see <see cref="HelpContent"/> and HelpWindow.</summary>
    public static string HelpTextEn { get; } = (@"TableNote {VERSION}

# Getting started
TableNote is a task table like Excel that reminds you when a task is due. It saves by itself after every change.
- Type the task in Title and Detail.
- Set Due with 📅 or type a date, e.g. 5/1/2026 9:00.
- Pick a status: Not started / In progress / Done.
- When it is due, a reminder window opens with a sound.
+ Row / − Row / + Column :: toolbar buttons (F5 / F6 / F7); delete a column with F8 or by right-clicking its header

# Cells (like Excel)
Click :: select a cell (gold)
Drag or Shift+click :: select a block of cells
Typing :: replaces the text of the selected cell
Double-click or F2 :: edit the text that is there (the caret goes to the end)
Esc :: puts back what you just typed
Enter / Shift+Enter :: keep it and go to the cell below / above
Tab / arrows :: next cell (Shift+arrows grow the block)
Alt+Enter :: a new line inside a cell
Delete :: clear the selected cells
Ctrl+A or the top-left corner :: select every cell
Click an empty spot :: nothing selected
Ctrl+Home / Ctrl+End :: first cell / last row
Formula bar :: above the table: the cell's address (B3) and its whole text; edit it there (no calculation); Enter keeps, Esc puts back
There is no fill handle (+) to drag.

# Rows and columns: select, insert, delete
Click a row number :: select the whole row (drag or Shift+click for several)
Click a column header :: select the whole column (drag or Shift+click for several)
Double-click a header, or F2 :: rename the column (Enter keeps, Esc puts the name back)
Right-click a row number :: insert rows above / below, delete rows, insert copied rows — as many as are selected
Right-click a column header :: insert columns left / right, delete columns, insert copied columns
Right-click a cell :: Insert ▸ / Delete ▸ rows or columns, and Insert copied cells
F5 / F6 :: add a row / delete the selected rows (asks first when they have text)
F7 / F8 :: add a column / delete the selected columns
Drag ⋮⋮ beside a row number :: move the row (select several rows first to move them together)
Drag ⋮⋮ on a header :: move the column
Alt+Shift+arrows :: move the row up / down, or the column left / right
Right-click a header → Column settings… :: name, width and type (text / number / choice / checkbox / days counter)
Right-click a row number → Row color / Highlight row :: a row color replaces the status color; a highlighted row gets an orange number

# Copy, paste and find
Ctrl+C / Ctrl+X :: copy / cut the selected cells (tab-separated, so they paste into Excel as they look)
Ctrl+V :: paste: a block from Excel starts at the selected cell; one value fills every selected cell
Insert copied :: copy whole rows or columns, then right-click → Insert copied rows / columns. The copy keeps its row color, repeat and advance reminders (or the column's type and width). Text from Excel goes in as new rows.
Ctrl+F :: find in the table; matches turn yellow (Enter / Shift+Enter or F3 / Shift+F3: next / previous; Esc closes)
File → Copy table / Copy with headers :: copy the whole table

# Undo and redo
Ctrl+Z :: undo — the last 100 steps are remembered
Ctrl+Y or Ctrl+Shift+Z :: redo (forward again)
Table → Undo / Redo :: also in the menu, with the name of the step
While you type in a cell, Ctrl+Z first takes back the letters in that cell.
Not undone with Ctrl+Z: alert boxes, and progress notes in the days pop-up.

# Reminders
Due :: calendar + HH:mm (24-hour); dates are CE (a B.E. year such as 09/11/2569 becomes 2026)
Typed dates :: yyyy-MM-dd HH:mm or day/month/year, e.g. 5/1/2026 9:00 = 5 Jan 2026 09:00
📅 :: opens the calendar (an empty cell gets now + 1 minute); Shift+click = tomorrow 09:00
Status :: Not started / In progress / Done (blank = Not started)
Repeat 🔁 :: None / Daily / Weekly / Monthly; Done keeps the row as history and adds the next one below
⏰ advance reminders :: 15 min / 1 hour / 1 day or your own, up to 5 per task (the on-time reminder always fires)
⚠ before a due date :: the date can't be read, so that task won't remind
Done statuses: done, complete, completed, ✓, ✔, เสร็จ, เสร็จแล้ว (the whole word).

# Reminder window and sound
Reminders that are due together gather in one window, with a sound.
Snooze 10 min :: reminds again in 10 minutes
Done :: sets the status (and adds the next round of a repeating task)
Acknowledge :: no more reminders for this time
Close :: acknowledges every task left in the window
X (window corner) :: snoozes every task left for 10 minutes
View → System → Reminder sound… :: the sound (Windows' sounds or your own .wav), the volume (50 % unless you change it; above 50 % it asks first), mute and a test button
Reminders work while TableNote runs — also when it is only in the tray.

# Colors
Light red :: overdue
Yellow :: due today
Faded and struck through :: done
Row color :: replaces the colors above (right-click a row number → Row color)
Hide done rows / Sort by due :: toolbar buttons

# Days counter and progress notes
Table → Add days counter column :: each row shows the days since its start (150 days); Done stops it (42 days ✓)
Click a days cell (or Space) :: the start date, the status and since when, the last update, and the row's history
Progress notes :: type one and press Enter to add it; Edit (or a double-click) changes it; ✕ then Delete? removes it
History is kept from the day the table got a days column. Exports keep the start date; pictures, print and the wallpaper show the days.

# Alert boxes
Table → Add alert box :: text boxes above the table; + Alert adds more
Drag ⋮⋮, or Up / Down :: change their order
Delete :: asks first; it can't be undone with Ctrl+Z
These boxes are separate from the due-date reminders.

# Closing the window and exiting
Close the window (X) or Alt+F4 :: TableNote goes to the tray (bottom-right) and keeps reminding you
File → Close window :: the same as X
File → Exit TableNote :: quits TableNote; no reminders until you start it again
Right-click the tray icon → Quit :: quits as well
Open it again :: click the tray icon, or start TableNote again
View → System → Launch at sign-in :: starts hidden in the tray when you sign in

# Activity bar (bottom)
The bar at the bottom of the window shows what TableNote is doing and what it did last.
Dot :: green = ready / done, yellow = working or about to save, orange = worth knowing, red = something failed
Progress bar :: moves while TableNote exports, saves a picture, prepares pages to print or draws the wallpaper
Last saved :: when note.json was saved last
History :: what TableNote did lately (opened the table, saved, reminders, undo, export …) with the times, and Open folder (where tablenote.log is)
✕ :: hides the bar; View → Activity bar shows it again (kept in settings.json)
If TableNote stops responding for more than 2 seconds, an orange line says for how long and what it was doing.

# Drawing on the table (layers)
Draw button or Table → Draw on the table :: turns drawing on; Done or Esc turns it off (the layers stay on the table)
Pen / Eraser / Ruler / Move (P / E / R / M) :: write freehand / erase lines on the active layer / straight line (Shift: 15° steps) / move a picture
Colour :: pick a swatch or type #RRGGBB and press Enter
Size and Sharpness :: line width; sharpness 0–100 % of the active layer (100 clear, 50 half faded) for its drawing and pictures
Clear all :: empties the active layer after asking
Ctrl+Z / Ctrl+Y while drawing :: undo / redo the drawing (not the table)
Layers :: tick to show / hide, click a name to draw on it; + Layer, Rename, Delete layer (up to 10); View → Note layers shows or hides them anytime
+ Picture :: puts a picture on the layer; with Move drag it, drag its corner to resize (Shift: any shape), Delete removes it
The wheel :: still scrolls the table while drawing; the drawing moves with the table

# Pictures in cells
Right-click a cell → Insert picture in cell… :: or Table → Picture in the selected cell…
Picture ▾ / ▸ in the cell :: shows or hides the picture under the cell's text; click the picture to see it whole
Right-click again :: Change picture…, Show / Hide picture, Remove picture…

# Wallpaper mode (Windows)
Wallpaper button or File → Set as wallpaper :: the table is drawn over your own wallpaper, full screen; the editor goes to the tray
The picture follows the table :: tasks marked done, tasks turning overdue, a new day
Back to editing :: click the tray icon, Tray → Edit table, Ctrl+Shift+E, or start TableNote again (your own wallpaper comes back)

# Size and language
Ctrl + / Ctrl − / Ctrl 0 :: larger / smaller / 100% (80–200%)
Ctrl+mouse wheel, or A− 100% A+ :: zoom
View → Language :: switch the language (kept in settings.json)

# Export, print and pictures
Export button :: .xlsx (Excel) / .csv (UTF-8) / .txt (tab-separated); status and repeat as words; dates as yyyy-MM-dd
File → Export image / Print :: a PNG / JPEG picture of the table, or print it as it looks
When done rows are hidden, you can choose to include them.

# Files and backups
Windows :: %APPDATA%\TableNote\note.json
Linux :: ~/.local/share/TableNote/note.json
TableNote saves by itself about 1.5 seconds after you stop typing.
settings.json :: language, zoom and sound, next to note.json
layers.json and images/ :: the note layers, pictures and pictures in cells, next to note.json
tablenote.log :: what the program did; 2 files, at most 512 KB in all
Backups :: older formats as note.v2.bak, note.v3.bak, note.v4.bak …; broken files as note.json*.bak (the newest 5)
Limits :: 1000 rows, 30 columns, 2,000 characters per cell, note.json up to 8 MB
").Replace("{VERSION}", Version);

    public static string HelpTextTh { get; } = (@"ตารางโน้ต รุ่น {VERSION}

# เริ่มต้นใช้งาน
ตารางโน้ตเป็นตารางงานแบบ Excel ที่เตือนเมื่อถึงกำหนดส่ง บันทึกให้อัตโนมัติทุกครั้งที่แก้ ไม่ต้องกดบันทึก
- พิมพ์งานในคอลัมน์ หัวข้อ และ รายละเอียด
- ตั้ง กำหนดส่ง ด้วยปุ่ม 📅 หรือพิมพ์วันที่ เช่น 5/1/2026 9:00
- เลือกสถานะ: ยังไม่เริ่ม / กำลังทำ / เสร็จแล้ว
- ถึงเวลาจะมีหน้าต่างเตือนเด้งขึ้นพร้อมเสียง
+ แถว / − แถว / + คอลัมน์ :: ปุ่มบนแถบเครื่องมือ (F5 / F6 / F7) ลบคอลัมน์ใช้ F8 หรือคลิกขวาที่หัวคอลัมน์

# เลือกช่องและแก้ไข (แบบ Excel)
คลิก :: เลือกช่อง (เป็นสีทอง)
ลาก หรือ Shift+คลิก :: คลุมหลายช่อง
พิมพ์ :: เขียนทับข้อความในช่องที่เลือก
ดับเบิลคลิก หรือ F2 :: แก้ข้อความเดิม (เคอร์เซอร์อยู่ท้ายข้อความ)
Esc :: ยกเลิกสิ่งที่เพิ่งพิมพ์ ข้อความเดิมกลับมา
Enter / Shift+Enter :: เก็บแล้วไปช่องข้างล่าง / ข้างบน
Tab / ลูกศร :: ไปช่องถัดไป (Shift+ลูกศร = ขยายการคลุม)
Alt+Enter :: ขึ้นบรรทัดใหม่ในช่อง
Delete :: ล้างข้อมูลในช่องที่เลือก
Ctrl+A หรือคลิกมุมซ้ายบน :: เลือกทุกช่อง
คลิกที่ว่าง :: ยกเลิกการเลือก
Ctrl+Home / Ctrl+End :: ไปช่องแรก / แถวสุดท้าย
แถบสูตร :: เหนือตาราง แสดงชื่อช่อง (เช่น B3) และข้อความทั้งหมด แก้ตรงนั้นได้ (ไม่คำนวณสูตร) Enter = เก็บ, Esc = คืนค่าเดิม
ไม่มีจุด + สำหรับลากเติมแบบ Excel

# เลือกแถวและคอลัมน์ แทรก ลบ
คลิกเลขแถว :: เลือกทั้งแถว (ลาก หรือ Shift+คลิก เพื่อเลือกหลายแถว)
คลิกหัวคอลัมน์ :: เลือกทั้งคอลัมน์ (ลาก หรือ Shift+คลิก เพื่อเลือกหลายคอลัมน์)
ดับเบิลคลิกหัวคอลัมน์ หรือ F2 :: เปลี่ยนชื่อคอลัมน์ (Enter = เก็บ, Esc = ชื่อเดิม)
คลิกขวาเลขแถว :: แทรกแถวด้านบน / ด้านล่าง ลบแถว แทรกแถวที่คัดลอก ตามจำนวนแถวที่เลือก
คลิกขวาหัวคอลัมน์ :: แทรกคอลัมน์ทางซ้าย / ขวา ลบคอลัมน์ แทรกคอลัมน์ที่คัดลอก
คลิกขวาที่ช่อง :: แทรก ▸ / ลบ ▸ แถวหรือคอลัมน์ และแทรกเซลล์ที่คัดลอก
F5 / F6 :: เพิ่มแถว / ลบแถวที่เลือก (ถามก่อนถ้ามีข้อมูล)
F7 / F8 :: เพิ่มคอลัมน์ / ลบคอลัมน์ที่เลือก
ลาก ⋮⋮ ข้างเลขแถว :: ย้ายแถว (เลือกหลายแถวก่อน แล้วลากไปพร้อมกันได้)
ลาก ⋮⋮ ที่หัวคอลัมน์ :: ย้ายคอลัมน์
Alt+Shift+ลูกศร :: ย้ายแถวขึ้น / ลง หรือย้ายคอลัมน์ซ้าย / ขวา
คลิกขวาหัวคอลัมน์ → ตั้งค่าคอลัมน์… :: ชื่อ ความกว้าง และชนิด (ข้อความ / ตัวเลข / ตัวเลือก / ช่องติ๊ก / นับวัน)
คลิกขวาเลขแถว → สีแถว / ไฮไลต์แถว :: สีแถวใช้แทนสีตามสถานะ ไฮไลต์แถว = เลขแถวเป็นสีส้ม

# คัดลอก วาง และค้นหา
Ctrl+C / Ctrl+X :: คัดลอก / ตัดช่องที่เลือก (คั่นด้วยแท็บ วางใน Excel ได้ตามที่เห็น)
Ctrl+V :: วาง ชุดข้อมูลจาก Excel เริ่มที่ช่องที่เลือก ค่าเดียวจะเติมทุกช่องที่เลือก
แทรกที่คัดลอก :: คัดลอกทั้งแถวหรือทั้งคอลัมน์ แล้วคลิกขวา → แทรกแถว / คอลัมน์ที่คัดลอก สำเนาจะมีสีแถว การทำซ้ำ และการเตือนล่วงหน้าด้วย (หรือชนิดและความกว้างของคอลัมน์) ข้อความจาก Excel จะแทรกเป็นแถวใหม่
Ctrl+F :: ค้นหาในตาราง ช่องที่เจอเป็นสีเหลือง (Enter / Shift+Enter หรือ F3 / Shift+F3 = ถัดไป / ก่อนหน้า, Esc = ปิด)
ไฟล์ → คัดลอกตาราง / คัดลอกพร้อมหัว :: คัดลอกทั้งตาราง

# ย้อนกลับและทำซ้ำ
Ctrl+Z :: ย้อนกลับ จำได้ 100 ครั้งล่าสุด
Ctrl+Y หรือ Ctrl+Shift+Z :: ทำซ้ำ (ไปข้างหน้า)
ตาราง → ย้อนกลับ / ทำซ้ำ :: ในเมนูก็มี และบอกชื่อการกระทำที่จะย้อน
ระหว่างพิมพ์ในช่อง Ctrl+Z จะย้อนตัวอักษรในช่องนั้นก่อน
ย้อนด้วย Ctrl+Z ไม่ได้: กล่องแจ้งเตือน และบันทึกความคืบหน้าในหน้าต่างนับวัน

# ตั้งเตือนงาน
กำหนดส่ง :: ปฏิทิน + เวลา HH:mm (24 ชั่วโมง) เก็บเป็นปี ค.ศ. (พิมพ์ปี พ.ศ. เช่น 09/11/2569 จะแปลงเป็น 2026)
พิมพ์วันที่ :: yyyy-MM-dd HH:mm หรือ วัน/เดือน/ปี เช่น 5/1/2026 9:00 = 5 ม.ค. 2026 09:00
📅 :: เปิดปฏิทิน (ช่องว่างจะตั้ง +1 นาทีเพื่อทดสอบ) Shift+คลิก = พรุ่งนี้ 09:00
สถานะ :: ยังไม่เริ่ม / กำลังทำ / เสร็จแล้ว (ช่องว่าง = ยังไม่เริ่ม)
ทำซ้ำ 🔁 :: ไม่ซ้ำ / ทุกวัน / ทุกสัปดาห์ / ทุกเดือน กดเสร็จแล้วแถวเดิมเป็นประวัติ และแทรกรอบถัดไปด้านล่าง
⏰ เตือนล่วงหน้า :: ก่อน 15 นาที / 1 ชั่วโมง / 1 วัน หรือกำหนดเอง ได้ 5 ครั้งต่องาน (เตือนตรงเวลายังมีเสมอ)
⚠ หน้าช่องกำหนดส่ง :: อ่านวันที่ไม่ได้ งานนั้นจะไม่เตือน
สถานะที่ถือว่าเสร็จ: เสร็จ, เสร็จแล้ว, done, complete, completed, ✓, ✔ (ตรงทั้งคำ)

# หน้าต่างเตือนและเสียง
งานที่ถึงเวลาพร้อมกันจะรวมอยู่ในหน้าต่างเดียว พร้อมเสียง
เลื่อน 10 นาที :: เตือนอีกครั้งในอีก 10 นาที
เสร็จแล้ว :: เปลี่ยนสถานะ และสร้างรอบถัดไปถ้าตั้งทำซ้ำไว้
รับทราบ :: ไม่เตือนซ้ำสำหรับเวลานี้
ปิด :: รับทราบทุกงานที่ค้างในหน้าต่าง
X (มุมหน้าต่าง) :: เลื่อน 10 นาทีทุกงานที่ค้าง
มุมมอง → ตั้งค่าระบบ → เสียงแจ้งเตือน… :: เลือกเสียง (ของ Windows หรือไฟล์ .wav ของเอง) ตั้งความดัง (เริ่มต้น 50% ถ้าเกิน 50% จะถามก่อน) ปิดเสียง และปุ่มทดลองฟัง
การเตือนทำงานเมื่อโปรแกรมเปิดอยู่ รวมตอนที่อยู่ที่ถาดไอคอน

# ความหมายของสี
แดงอ่อน :: เลยกำหนดส่งแล้ว
เหลือง :: ครบกำหนดภายในวันนี้
เทาจาง + ขีดฆ่า :: เสร็จแล้ว
สีแถวที่ตั้งเอง :: ใช้แทนสีข้างบน (คลิกขวาที่เลขแถว → สีแถว)
ซ่อนงานเสร็จ / เรียงตามกำหนด :: ปุ่มบนแถบเครื่องมือ

# นับวันและบันทึกความคืบหน้า
ตาราง → เพิ่มคอลัมน์นับวัน :: แต่ละแถวแสดงว่ารับงานมากี่วัน (150 วัน) ตั้งเป็นเสร็จแล้วจะหยุดนับ (42 วัน ✓)
คลิกช่องนับวัน (หรือกด Space) :: ดูวันรับงาน สถานะและตั้งแต่เมื่อไร อัปเดตล่าสุด และประวัติของแถว
บันทึกความคืบหน้า :: พิมพ์แล้วกด Enter เพื่อเพิ่ม กด แก้ไข (หรือดับเบิลคลิก) เพื่อแก้ กด ✕ แล้ว ลบ? เพื่อลบ
ประวัติเริ่มเก็บตั้งแต่ตารางมีคอลัมน์นับวัน ส่งออกไฟล์เก็บเป็นวันรับงาน ส่วนภาพ งานพิมพ์ และวอลเปเปอร์แสดงเป็นจำนวนวัน

# กล่องแจ้งเตือน
ตาราง → เพิ่มกล่องแจ้งเตือน :: กล่องข้อความเหนือตาราง ปุ่ม + กล่องแจ้งเตือน เพิ่มกล่องต่อไป
ลาก ⋮⋮ หรือปุ่ม ขึ้น / ลง :: เรียงลำดับกล่อง
ลบ :: ถามก่อน และย้อนกลับด้วย Ctrl+Z ไม่ได้
กล่องนี้คนละอย่างกับการเตือนตามกำหนดส่ง

# ปิดหน้าต่างและออกจากโปรแกรม
ปิดหน้าต่าง (X) หรือ Alt+F4 :: ไปอยู่ที่ถาดไอคอน (มุมขวาล่าง) ตารางโน้ตยังเตือนงานต่อ
ไฟล์ → ปิดหน้าต่าง :: เหมือนกด X
ไฟล์ → ออกจากโปรแกรม :: ปิดโปรแกรมจริง จะไม่มีการเตือนจนกว่าจะเปิดใหม่
คลิกขวาไอคอนในถาด → ออก :: ปิดโปรแกรมจริงเหมือนกัน
เปิดกลับ :: คลิกไอคอนในถาด หรือเปิดโปรแกรมอีกครั้ง
มุมมอง → ตั้งค่าระบบ → เปิดเมื่อเข้าสู่ระบบ :: เริ่มแบบซ่อนอยู่ที่ถาดทุกครั้งที่เข้าเครื่อง

# แถบการทำงาน (ด้านล่าง)
แถบล่างสุดของหน้าต่างบอกว่าตารางโน้ตกำลังทำอะไร และทำอะไรล่าสุด
จุดสี :: เขียว = พร้อม / เสร็จแล้ว, เหลือง = กำลังทำงานหรือรอบันทึก, ส้ม = มีเรื่องควรรู้, แดง = ทำไม่สำเร็จ
หลอดความคืบหน้า :: วิ่งตอนกำลังส่งออก บันทึกภาพ เตรียมหน้าพิมพ์ หรือวาดวอลเปเปอร์
บันทึกล่าสุด :: เวลาที่บันทึก note.json ครั้งล่าสุด
ประวัติ :: สิ่งที่โปรแกรมทำล่าสุด (เปิดตาราง บันทึก แจ้งเตือน ย้อนกลับ ส่งออก …) พร้อมเวลา และปุ่มเปิดโฟลเดอร์ที่มี tablenote.log
✕ :: ซ่อนแถบ เปิดอีกครั้งที่ มุมมอง → แถบการทำงาน (จำไว้ใน settings.json)
ถ้าโปรแกรมไม่ตอบสนองเกิน 2 วินาที จะมีบรรทัดสีส้มบอกว่าค้างกี่วินาที และขณะทำอะไรอยู่

# วาดเขียนบนตาราง (เลเยอร์)
ปุ่ม วาดเขียน หรือ ตาราง → วาดเขียนบนตาราง :: เปิดโหมดวาด กด เสร็จ หรือ Esc เพื่อหยุด (เลเยอร์ยังแสดงบนตาราง)
ปากกา / ยางลบ / ไม้บรรทัด / ย้าย (P / E / R / M) :: เขียนอิสระ / ลบเส้นในเลเยอร์ที่เลือก / เส้นตรง (Shift ล็อกมุมทีละ 15°) / ย้ายรูปภาพ
สี :: คลิกเลือกสี หรือพิมพ์ #RRGGBB แล้วกด Enter
ขนาดและความคมชัด :: ความหนาของเส้น ความคมชัด 0–100% ของเลเยอร์ที่เลือก (100 = ชัด 50 = จางครึ่งหนึ่ง) ทั้งเส้นและรูปภาพ
ลบทั้งหมด :: ถามก่อนแล้วล้างเลเยอร์ที่เลือก
Ctrl+Z / Ctrl+Y ระหว่างวาด :: ย้อนกลับ / ทำซ้ำภาพวาด (ไม่ใช่ตาราง)
เลเยอร์ :: ติ๊กเพื่อเปิด/ปิด คลิกชื่อเพื่อวาดบนเลเยอร์นั้น + เลเยอร์ เปลี่ยนชื่อ ลบเลเยอร์ (สูงสุด 10) มุมมอง → เลเยอร์โน้ต เปิด/ปิดได้ทุกเวลา
+ รูปภาพ :: วางภาพบนเลเยอร์ ใช้ ย้าย ลากเพื่อย้าย ลากมุมเพื่อย่อ/ขยาย (Shift ไม่คงสัดส่วน) Delete ลบ
ล้อเมาส์ :: ยังเลื่อนตารางได้ระหว่างวาด ภาพวาดเลื่อนตามตาราง

# ภาพในช่อง
คลิกขวาที่ช่อง → ใส่ภาพในช่อง… :: หรือ ตาราง → ภาพในช่องที่เลือก…
ปุ่ม ภาพ ▾ / ▸ ในช่อง :: เปิดหรือปิดภาพใต้ข้อความในช่อง คลิกภาพเพื่อดูเต็ม
คลิกขวาอีกครั้ง :: เปลี่ยนภาพ… แสดง / ซ่อนภาพ เอาภาพออก…

# โหมดวอลเปเปอร์ (Windows)
ปุ่ม วอลเปเปอร์ หรือ ไฟล์ → ตั้งเป็นวอลเปเปอร์ :: วาดตารางทับวอลเปเปอร์ของคุณแบบเต็มจอ แล้วหน้าแก้ไขไปอยู่ที่ถาด
ภาพอัปเดตตามตาราง :: งานที่เสร็จ งานที่เลยกำหนด และวันใหม่
กลับมาแก้ไข :: คลิกไอคอนที่ถาด, ถาด → แก้ไขตาราง, Ctrl+Shift+E หรือเปิดโปรแกรมอีกครั้ง (วอลเปเปอร์เดิมกลับมาเอง)

# ขนาดตัวอักษรและภาษา
Ctrl + / Ctrl − / Ctrl 0 :: ขยาย / ย่อ / กลับ 100% (80–200%)
Ctrl+ล้อเมาส์ หรือปุ่ม A− 100% A+ :: ขยายหรือย่อ
มุมมอง → ภาษา :: สลับภาษา (จำไว้ใน settings.json)

# ส่งออก พิมพ์ และภาพ
ปุ่ม ส่งออก :: .xlsx (Excel) / .csv (UTF-8) / .txt (คั่นด้วยแท็บ) สถานะและทำซ้ำเป็นคำตามภาษา วันที่เป็น yyyy-MM-dd
ไฟล์ → ส่งออกภาพ / พิมพ์ :: ภาพ PNG / JPEG ของตาราง หรือสั่งพิมพ์ตามที่เห็น
ถ้าซ่อนงานเสร็จอยู่ จะถามว่าจะรวมแถวที่ซ่อนหรือไม่

# ที่เก็บไฟล์และการสำรอง
Windows :: %APPDATA%\TableNote\note.json
Linux :: ~/.local/share/TableNote/note.json
บันทึกอัตโนมัติหลังหยุดพิมพ์ประมาณ 1.5 วินาที
settings.json :: ภาษา ขนาด และเสียง อยู่ข้าง note.json
layers.json และ images/ :: เลเยอร์โน้ต รูปภาพ และภาพในช่อง อยู่ข้าง note.json
tablenote.log :: บันทึกการทำงานของโปรแกรม เก็บ 2 ไฟล์ รวมไม่เกิน 512 KB
ไฟล์สำรอง :: รูปแบบเก่าเป็น note.v2.bak, note.v3.bak, note.v4.bak … ไฟล์เสียเก็บเป็น note.json*.bak ล่าสุด 5 ไฟล์
ขีดจำกัด :: 1000 แถว 30 คอลัมน์ ช่องละ 2,000 ตัวอักษร note.json ไม่เกิน 8 MB
").Replace("{VERSION}", Version);

    public static string LogTextEn { get; } =
@"TableNote " + Version + @" — built 23 Nov 2026 (Bangkok time, UTC+7)

23 Nov 2026 — 2.6.1.1
New: draw on the table in layers (Draw on the toolbar): pen, eraser, ruler (Shift keeps 15° steps), Clear all, pen colour (11 swatches or any #RRGGBB), size, sharpness 0–100 % per layer (100 clear, lower more faded), undo / redo, and pictures you can move and resize. Up to 10 layers, each shown or hidden with a tick, also from View → Note layers.
New: pictures in cells: right-click a cell → Insert picture in cell…; the Picture ▾ / ▸ button in the cell shows or hides it. Kept in layers.json and the images folder next to note.json.

22 Nov 2026 — 2.6.1
Faster and lighter: the program's code is now compiled ahead of time inside the exe, so TableNote opens about 3 to 4 times sooner and uses less CPU and memory after it starts. The loading screen no longer waits 1.2 s when the table is ready sooner (at most 0.3 s).
Changed: table cells are lighter (far fewer parts each), so the table is built and scrolled with less work. The scroll bar that showed inside a cell with long text (a thin grey line, or a bar with arrows while editing) is gone; the text still scrolls with the caret.
New: tablenote.log has a start timeline (program code started, loading screen drawn, table built, editor drawn), so a slow start can be measured on any PC.
The download is bigger (zip about 29 MB, exe about 69 MB; was 18 and 23 MB): the precompiled code is what makes the start fast.

21 Nov 2026 — 2.6.0
New: Wallpaper (toolbar) puts the table on your desktop wallpaper, full screen, over your own picture and where the window was. The tray icon or Ctrl+Shift+E brings the editor back, and your own wallpaper with it. This replaces Pin to desktop.
Changed: one toolbar row with the everyday buttons. Copy, image export, print, delete column, language and the system settings are in the menu bar. The alert panel shows only when there are alert boxes (Table → Add alert box). Zoom scales the table, not the menu and toolbar.
New: activity bar at the bottom (View → Activity bar): what TableNote is doing now (a progress bar while it exports, prints or draws the wallpaper), the last thing it did, when it last saved, and a History list. ✕ hides it.
New: whole rows and columns: click a row number or a header (drag / Shift+click for several); right-click to insert, delete or insert copied rows / columns; drag ⋮⋮ beside a row number to move rows. Table → Undo / Redo. File → Close window (to the tray) and File → Exit TableNote. Help in topics that open and close.
New: Excel-style cells: click selects (gold), drag selects a block, typing replaces, double-click or F2 edits, Delete clears, Ctrl+C / X / V copy, cut and paste blocks to and from Excel, Ctrl+A selects everything.
New: formula bar above the table (the cell's address and whole text, no calculation); Ctrl+F finds text in the table.
New: View → System → Reminder sound…: pick the sound, set the volume (asks above 50 %), mute, test. The Table Note logo is the program's icon.
New: a loading screen with the Table Note logo while TableNote opens. Starting TableNote while a copy from another folder is running (an older version in the tray) asks whether to close it and start this one.
New: days counter column (Table → Add days counter column). Each row shows how many days since its start date; click the cell for status updates, edits and progress notes (add, edit, delete) with their dates and times.
Fixed: Thai text, digits, Latin letters and symbols now use one font (Noto Sans Thai with Noto Sans merged in); before, ""17/11/2569"" or ""A+"" switched to another font in the middle of a line.
Fixed: after a sign-in start, opening the editor from the tray closed it again at once.

19 Nov 2026 — 2.5.1
Fixed: a due date with a month name or a two-digit year (5 Jan 2026, 5-Jan-26, 5/1/26), e.g. pasted from Excel, was read as 1483 or 1983 and reminded at once as missed.
Fixed: with Hide done on, pasting several lines wrote into the hidden done rows.
Fixed: Ctrl+Z after deleting a row lost its detail tables; after deleting a column, its colours; after deleting the Due column, acknowledged reminders popped up again.
Fixed: if another program was using note.json at start-up, the whole session ran on an empty table with no reminders, and the launch-at-sign-in entry was removed. TableNote now opens note.json as soon as it can be read and leaves that setting alone.
Fixed: without a tray icon, closing the window left TableNote running invisibly; it now exits.
Changed: the silent Windows toast is removed (the reminder window and its sound are unchanged). TableNote.exe is about 24 MB instead of 56 MB.

17 Nov 2026 — 2.5.0
New: collapsible column groups, a detail table on chosen cells, alert boxes above the table, RGB colours for cells, headers, rows and sections, typed column width and row height, image export and printing.
New: toolbar groups and a menu bar (File / Table / Column / Row / View / Help); the Status and Due columns can be deleted and added back.
Compatibility: note.json format version 6. A 2.4.2 file opens, and the first save writes version 6.

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

23 พ.ย. 2026 — 2.6.1.1
ใหม่: วาดเขียนบนตารางเป็นเลเยอร์ (ปุ่ม วาดเขียน): ปากกา ยางลบ ไม้บรรทัด (กด Shift ล็อกมุมทีละ 15°) ลบทั้งหมด สีปากกา (11 สี หรือพิมพ์ #RRGGBB) ขนาด ความคมชัด 0–100% ของแต่ละเลเยอร์ (100 = ชัด ค่าน้อยลง = จางลง) ย้อนกลับ / ทำซ้ำ และรูปภาพที่ย้ายและย่อ/ขยายได้ สูงสุด 10 เลเยอร์ เปิด/ปิดแต่ละเลเยอร์ด้วยช่องติ๊ก หรือที่ มุมมอง → เลเยอร์โน้ต
ใหม่: ภาพในช่อง: คลิกขวาที่ช่อง → ใส่ภาพในช่อง… แล้วกดปุ่ม ภาพ ▾ / ▸ ในช่องเพื่อเปิด/ปิดภาพ เก็บใน layers.json และโฟลเดอร์ images ข้าง note.json

22 พ.ย. 2026 — 2.6.1
เร็วขึ้นและเบาลง: โค้ดของโปรแกรมแปลงเป็นภาษาเครื่องไว้ล่วงหน้าในไฟล์ exe แล้ว โปรแกรมจึงเปิดเร็วขึ้นราว 3–4 เท่า และหลังเปิดใช้ CPU กับหน่วยความจำน้อยลง หน้าโหลดไม่ต้องรอครบ 1.2 วินาทีเมื่อตารางพร้อมก่อน (รอไม่เกิน 0.3 วินาที)
เปลี่ยน: ช่องในตารางเบาลง (ชิ้นส่วนในแต่ละช่องน้อยลงมาก) สร้างและเลื่อนตารางได้โดยใช้แรงเครื่องน้อยลง แถบเลื่อนที่เคยขึ้นในช่องที่ข้อความยาว (เส้นเทาบาง ๆ หรือแถบมีลูกศรตอนแก้ไข) หายไปแล้ว ข้อความยังเลื่อนตามเคอร์เซอร์เหมือนเดิม
ใหม่: tablenote.log บันทึกเวลาของแต่ละขั้นตอนตอนเปิดโปรแกรม (โค้ดเริ่มทำงาน หน้าโหลดขึ้น สร้างตารางเสร็จ หน้าแก้ไขขึ้น) ไว้ดูว่าเครื่องไหนเปิดช้าที่ขั้นตอนใด
ไฟล์ดาวน์โหลดใหญ่ขึ้น (zip ราว 29 MB, exe ราว 69 MB เดิม 18 และ 23 MB) เพราะเก็บโค้ดที่แปลงไว้ล่วงหน้า ซึ่งเป็นสิ่งที่ทำให้เปิดเร็ว

21 พ.ย. 2026 — 2.6.0
ใหม่: ปุ่ม วอลเปเปอร์ ตั้งตารางเป็นวอลเปเปอร์เต็มจอ ทับภาพเดิมของคุณตรงตำแหน่งหน้าต่าง คลิกไอคอนที่ถาดหรือกด Ctrl+Shift+E เพื่อกลับมาแก้ไข แล้ววอลเปเปอร์เดิมจะกลับมา ใช้แทน วางแนบพื้นหลัง
เปลี่ยน: แถบเครื่องมือเหลือแถวเดียวเฉพาะปุ่มที่ใช้บ่อย คัดลอก ส่งออกภาพ พิมพ์ ลบคอลัมน์ ภาษา และตั้งค่าระบบ อยู่ในแถบเมนู ช่องกล่องแจ้งเตือนขึ้นเมื่อมีกล่องเท่านั้น (ตาราง → เพิ่มกล่องแจ้งเตือน) ปุ่มขยาย/ย่อขยายเฉพาะตาราง ไม่ขยายเมนูและแถบเครื่องมือ
ใหม่: แถบการทำงานด้านล่าง (มุมมอง → แถบการทำงาน) บอกว่าโปรแกรมกำลังทำอะไร (มีหลอดความคืบหน้าตอนส่งออก พิมพ์ หรือวาดวอลเปเปอร์) ทำอะไรล่าสุด บันทึกล่าสุดเมื่อไร และมีปุ่ม ประวัติ กด ✕ เพื่อซ่อน
ใหม่: เลือกทั้งแถว / ทั้งคอลัมน์ (คลิกเลขแถวหรือหัวคอลัมน์ ลากหรือ Shift+คลิกเลือกหลายอัน) คลิกขวาเพื่อแทรก ลบ หรือแทรกแถว / คอลัมน์ที่คัดลอก ลาก ⋮⋮ ข้างเลขแถวเพื่อย้าย ตาราง → ย้อนกลับ / ทำซ้ำ ไฟล์ → ปิดหน้าต่าง (ไปที่ถาด) และ ไฟล์ → ออกจากโปรแกรม วิธีใช้แบ่งเป็นหัวข้อ คลิกเพื่อขยาย / ยุบ
ใหม่: ช่องแบบ Excel: คลิก = เลือก (สีทอง) ลากคลุมหลายช่อง พิมพ์ = เขียนทับ ดับเบิลคลิก/F2 = แก้ Delete = ล้าง Ctrl+C / X / V คัดลอก ตัด วาง ไป-มากับ Excel Ctrl+A = เลือกทั้งหมด
ใหม่: แถบสูตรเหนือตาราง (ที่อยู่ช่องและข้อความทั้งหมด ไม่คำนวณ) และ Ctrl+F ค้นหาในตาราง
ใหม่: มุมมอง → ตั้งค่าระบบ → เสียงแจ้งเตือน… เลือกเสียง ตั้งความดัง (เกิน 50% ถามก่อน) ปิดเสียง ทดลองฟัง และโลโก้ Table Note เป็นไอคอนโปรแกรม
ใหม่: หน้าโหลดมีโลโก้ Table Note ตอนเปิดโปรแกรม ถ้าเปิดตารางโน้ตขณะที่อีกตัวจากโฟลเดอร์อื่นเปิดอยู่ (เช่น รุ่นเก่าที่ถาด) จะถามว่าจะปิดตัวนั้นแล้วเปิดตัวนี้แทนไหม
ใหม่: คอลัมน์นับวัน (ตาราง → เพิ่มคอลัมน์นับวัน) แต่ละแถวแสดงว่ารับงานมากี่วันแล้ว คลิกที่ช่องเพื่อดูการอัปเดตสถานะ การแก้ไข และบันทึกความคืบหน้า (เพิ่ม แก้ไข ลบได้) พร้อมวันที่และเวลา
แก้: ตัวอักษรไทย ตัวเลข อังกฤษ และสัญลักษณ์ ใช้ฟอนต์เดียวกันแล้ว (Noto Sans Thai รวมกับ Noto Sans) ก่อนหน้านี้ ""17/11/2569"" หรือ ""A+"" สลับไปใช้อีกฟอนต์กลางบรรทัด
แก้: หลังเปิดเครื่องแบบเริ่มที่ถาด กดเปิดหน้าแก้ไขจากถาดแล้วหน้าต่างหายไปทันที

19 พ.ย. 2026 — 2.5.1
แก้: กำหนดส่งที่มีชื่อเดือนหรือปีสองหลัก (5 Jan 2026, 5-Jan-26, 5/1/26) เช่น วางจาก Excel ถูกอ่านเป็นปี 1483 หรือ 1983 และเด้งเตือนว่าพลาดทันที
แก้: ติ๊ก ""ซ่อนงานเสร็จ"" แล้ววางหลายบรรทัด ข้อมูลลงไปในแถวงานเสร็จที่ซ่อนอยู่
แก้: Ctrl+Z หลังลบแถวแล้วตารางรายละเอียดหาย หลังลบคอลัมน์แล้วสีหาย หลังลบคอลัมน์กำหนดส่งแล้วเตือนที่รับทราบไปแล้วเด้งใหม่
แก้: ถ้าตอนเปิดโปรแกรม note.json ถูกโปรแกรมอื่นใช้อยู่ ทั้งรอบจะเป็นตารางว่างที่ไม่เตือน และรายการเปิดเมื่อเข้าสู่ระบบถูกลบ ตอนนี้เปิด note.json ทันทีที่อ่านได้ และไม่แตะค่านั้น
แก้: ถ้าไม่มีไอคอนในถาด ปิดหน้าต่างแล้วโปรแกรมค้างอยู่เบื้องหลัง ตอนนี้ออกจากโปรแกรม
เปลี่ยน: เอา toast ของ Windows ที่ไม่มีเสียงออก (หน้าต่างเตือนและเสียงเหมือนเดิม) TableNote.exe เหลือราว 24 MB จาก 56 MB

17 พ.ย. 2026 — 2.5.0
ใหม่: กลุ่มคอลัมน์ยุบได้ ตารางรายละเอียดในช่อง กล่องแจ้งเตือนเหนือตาราง สี RGB ของช่อง หัวคอลัมน์ แถว และส่วนต่างๆ กำหนดความกว้างคอลัมน์และความสูงแถว ส่งออกภาพและพิมพ์
ใหม่: ปุ่มบนแถบเครื่องมือแบ่งเป็นกลุ่ม มีแถบเมนู (ไฟล์ / ตาราง / คอลัมน์ / แถว / มุมมอง / ช่วยเหลือ) ลบคอลัมน์สถานะและกำหนดส่งแล้วเพิ่มกลับได้
ความเข้ากันได้: note.json รูปแบบรุ่น 6 ไฟล์ 2.4.2 เปิดได้ และบันทึกครั้งแรกเป็นรุ่น 6

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
