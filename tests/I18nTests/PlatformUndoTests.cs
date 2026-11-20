using TableNote;

// v2.4.2: recovery notices, start-at-sign-in errors and undo labels follow the UI language. 2.6.0: wallpaper mode
// replaced the desktop-attached window, so its notices are wallpaper ones.
public static partial class T
{
    static Dictionary<string, string?> PlatformTexts()
    {
        var routesNoTray = new RecoveryRoutes(false, true);
        var routesNoSignal = new RecoveryRoutes(true, false);
        return new()
        {
            ["block_tray"] = RecoveryGate.BlockReason(routesNoTray),
            ["block_signal"] = RecoveryGate.BlockReason(routesNoSignal),
            ["forced_edit"] = RecoveryGate.StartupNotice(ViewMode.Background, true, routesNoTray),
            ["persisted_unsafe"] = RecoveryGate.StartupNotice(ViewMode.Background, false, routesNoTray),
            ["tray_lost"] = RecoveryGate.TrayLost,
            ["hotkey_unavailable"] = RecoveryHotkey.FailureNotice(false, false),
            ["hotkey_collision"] = RecoveryHotkey.FailureNotice(false, true),
            ["wallpaper_unsupported"] = new WallpaperMode(new UnsupportedWallpaperHost(), Directory.CreateTempSubdirectory("tn-i18n-wp").FullName)
                .Apply(default, [], _ => null).Message,
            ["wallpaper_more"] = Localizer.T("wallpaper.more_rows", 3),
            ["wallpaper_spotlight"] = Localizer.T("wallpaper.spotlight_confirm"),
            ["shell_detail"] = new UnsupportedWallpaperHost().Read(out var d) == null ? d : null,
            ["undo_cell"] = new CellEditAction("r", 0, "a", "b").Label,
            ["undo_header"] = new HeaderEditAction(0, "a", "b").Label,
            ["undo_move_row"] = new RowMoveAction("r", 0, 1).Label,
            ["undo_row_color"] = Localizer.T("undo.row_color", Localizer.T("colour.yellow")),
        };
    }

    public static void TestPlatformAndUndoText()
    {
        foreach (var (k, v) in In("en", PlatformTexts)) { Ok("[en] " + k + " not empty", !string.IsNullOrEmpty(v)); NoThai("[en] " + k, v); }
        foreach (var (k, v) in Localizer.Table("en").Where(kv => kv.Key.StartsWith("undo.", StringComparison.Ordinal) || kv.Key.StartsWith("recovery.", StringComparison.Ordinal) || kv.Key.StartsWith("wallpaper.", StringComparison.Ordinal)))
            NoThai("[en] " + k, v);
        var th = In("th", PlatformTexts);
        var expected = new Dictionary<string, string>
        {
            ["block_tray"] = "ยังไม่มีไอคอนที่ถาด จึงไม่เข้าโหมดวอลเปเปอร์ เพื่อไม่ให้กลับมาแก้ไขไม่ได้",
            ["forced_edit"] = "เปิดหน้าแก้ไขตาม --edit และไม่ใช้โหมดวอลเปเปอร์ที่จำไว้",
            ["hotkey_collision"] = "Ctrl+Shift+E ถูกโปรแกรมอื่นใช้อยู่ จึงไม่นับเป็นทางกลับ",
            ["wallpaper_unsupported"] = "โหมดวอลเปเปอร์ใช้ได้บน Windows เท่านั้น",
            ["wallpaper_more"] = "+ อีก 3 แถว",
            ["undo_cell"] = "แก้ช่อง", ["undo_header"] = "แก้หัวคอลัมน์", ["undo_move_row"] = "ย้ายแถว", ["undo_row_color"] = "สีแถว: เหลือง",
        };
        foreach (var (k, v) in expected) Ok("[th] " + k + " = agreed wording", th[k] == v, th[k] ?? "(null)");
        Ok("shell detail (log only) is English in TH too", !HasThai(th["shell_detail"]), th["shell_detail"] ?? "");
        Ok("explicit undo label kept", new CellEditAction("r", 0, "a", "b", "custom").Label == "custom");
    }
}
