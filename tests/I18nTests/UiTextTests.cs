using TableNote;

// v2.4.2: status bar / popup text in MainWindow is built from ui.* keys. The window itself needs a display,
// so these tests check the composed strings: Thai output = the 2.4.1 text, English output has no Thai.
public static partial class T
{
    static Dictionary<string, string> UiSamples() => new()
    {
        ["tray_notice"] = Localizer.T("ui.tray_notice_body", Localizer.T("tray.edit"), Localizer.T("tray.quit")),
        ["bg_bottom"] = "N " + Localizer.T("ui.bg_return_bottom", Localizer.T("tray.edit")) + " " + Localizer.T("ui.bg_return_hotkey"),
        ["bg_desktop"] = "N " + Localizer.T("ui.bg_return_desktop", Localizer.T("tray.edit")),
        ["save_failed"] = Localizer.T("ui.save_failed_body", Localizer.T("ui.save_failed")),
        ["alarm"] = Localizer.T("ui.alarm_tip_offsets", Localizer.T("remind.before", "X")),
        ["names"] = Localizer.T("ui.snoozed", Localizer.T("ui.names_many", 4, "\"a\", \"b\", \"c\" …")),
        ["paste"] = Localizer.T("ui.paste_header_too_many", 3, 1),
        ["path"] = Localizer.T("ui.saved_path_log", "/p/note.json", "/p/tablenote.log"),
        ["row_task"] = Localizer.T("ui.row_task", 2),
    };

    public static void TestUiTextThaiUnchanged()
    {
        var th = In("th", UiSamples);
        var expected = new Dictionary<string, string>
        {
            ["tray_notice"] = "ปิดหน้าต่างแล้ว แต่ตารางโน้ตยังทำงานอยู่ที่ถาดไอคอน (มุมขวาล่าง) เพื่อให้แจ้งเตือนงานต่อได้\n\n" +
                              "เปิดกลับ: คลิกไอคอนในถาด → แก้ไขตาราง หรือเปิดโปรแกรมอีกครั้ง\n" +
                              "ออกจริง: คลิกขวาไอคอนในถาด → ออก (ข้อความนี้แสดงครั้งเดียว)",
            ["bg_bottom"] = "N กลับได้จากถาดเมนู แก้ไขตาราง, เปิดโปรแกรมอีกครั้ง, หรือ --edit หรือ Ctrl+Shift+E",
            ["bg_desktop"] = "N Win+D ไม่ซ่อนตารางนี้เพราะฝังกับพื้นโต๊ะ กลับได้จากถาดเมนู แก้ไขตาราง, เปิดโปรแกรมอีกครั้ง, หรือ --edit",
            ["save_failed"] = "บันทึกไม่สำเร็จ\n\nยังไม่ปิดโปรแกรม เพื่อไม่ให้ข้อมูลที่ยังไม่บันทึกหายไป",
            ["alarm"] = "เตือนล่วงหน้า: ก่อน X + ตรงเวลา",
            ["names"] = "เลื่อนแจ้งเตือน 4 งาน: \"a\", \"b\", \"c\" … 10 นาที",
            ["paste"] = "วางชื่อคอลัมน์ไม่ได้ เพราะวางมา 3 ชื่อ แต่ตั้งแต่คอลัมน์นี้ไปมี 1 คอลัมน์",
            ["path"] = "บันทึกที่ /p/note.json\nรายละเอียดค่าที่อ่านไม่ได้: /p/tablenote.log",
            ["row_task"] = "งานแถว 2",
        };
        foreach (var (k, v) in expected) Ok("[th] ui." + k + " = 2.4.1 text", th[k] == v, th[k]);
    }

    public static void TestUiTextEnglish()
    {
        foreach (var (k, v) in In("en", UiSamples)) NoThai("[en] ui." + k, v);
        foreach (var (k, v) in Localizer.Table("en").Where(kv => kv.Key.StartsWith("ui.", StringComparison.Ordinal))) NoThai("[en] " + k, v);
    }
}
