using TableNote;

// v2.4.2: background/desktop/recovery notices, start-at-sign-in errors and undo labels follow the UI language.
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
            ["click"] = RecoveryGate.ClickThroughFailed, ["rollback"] = RecoveryGate.PartialRollback, ["tray_lost"] = RecoveryGate.TrayLost,
            ["taskbar"] = RecoveryGate.TaskbarHideFailed, ["not_attached"] = RecoveryGate.NotAttachedStaysEditor,
            ["hotkey_unavailable"] = RecoveryHotkey.FailureNotice(false, false),
            ["hotkey_collision"] = RecoveryHotkey.FailureNotice(false, true),
            ["desk_none"] = DesktopPlacement.Decide(new DesktopAttachResult(false, DesktopHostKind.None, "x")).Notice,
            ["desk_worker"] = DesktopPlacement.Decide(new DesktopAttachResult(true, DesktopHostKind.WorkerWBehindIcons, null)).Notice,
            ["desk_icon"] = DesktopPlacement.Decide(new DesktopAttachResult(true, DesktopHostKind.IconHost, null)).Notice,
            ["desk_progman"] = DesktopPlacement.Decide(new DesktopAttachResult(true, DesktopHostKind.Progman, null)).Notice,
            ["desk_bottom"] = DesktopPlacement.Decide(new DesktopAttachResult(true, DesktopHostKind.BottomFallback, null)).Notice,
            ["floating"] = DesktopPlacement.FloatingNotice,
            ["shell_detail"] = new UnsupportedDesktopShell().TryAttach(0).Detail,
            ["undo_cell"] = new CellEditAction("r", 0, "a", "b").Label,
            ["undo_header"] = new HeaderEditAction(0, "a", "b").Label,
            ["undo_move_row"] = new RowMoveAction("r", 0, 1).Label,
            ["undo_row_color"] = Localizer.T("undo.row_color", Localizer.T("colour.yellow")),
        };
    }

    public static void TestPlatformAndUndoText()
    {
        foreach (var (k, v) in In("en", PlatformTexts)) { Ok("[en] " + k + " not empty", !string.IsNullOrEmpty(v)); NoThai("[en] " + k, v); }
        foreach (var (k, v) in Localizer.Table("en").Where(kv => kv.Key.StartsWith("undo.", StringComparison.Ordinal) || kv.Key.StartsWith("recovery.", StringComparison.Ordinal) || kv.Key.StartsWith("desktop.", StringComparison.Ordinal)))
            NoThai("[en] " + k, v);
        var th = In("th", PlatformTexts);
        var expected = new Dictionary<string, string>
        {
            ["block_tray"] = "ยังไม่มีเมนูถาด จึงไม่เข้าโหมดพื้นหลัง เพื่อไม่ให้กลับไปแก้ไขไม่ได้",
            ["forced_edit"] = "เปิดหน้าแก้ไขตาม --edit และไม่ใช้โหมดพื้นหลังที่จำไว้",
            ["hotkey_collision"] = "Ctrl+Shift+E ถูกโปรแกรมอื่นใช้อยู่ จึงไม่นับเป็นทางกลับ",
            ["desk_none"] = "ติดพื้นโต๊ะไม่ได้ ตอนนี้เป็นหน้าต่างปกติ ไม่ได้อยู่เหนือโปรแกรมอื่น และไม่ใช่โหมดพื้นโต๊ะ",
            ["desk_icon"] = "ติดพื้นโต๊ะแล้ว (ใต้ไอคอน) อยู่หลังหน้าต่างโปรแกรม",
            ["undo_cell"] = "แก้ช่อง", ["undo_header"] = "แก้หัวคอลัมน์", ["undo_move_row"] = "ย้ายแถว", ["undo_row_color"] = "สีแถว: เหลือง",
        };
        foreach (var (k, v) in expected) Ok("[th] " + k + " = 2.4.1 text", th[k] == v, th[k] ?? "(null)");
        Ok("shell detail (log only) is English in TH too", !HasThai(th["shell_detail"]), th["shell_detail"] ?? "");
        Ok("explicit undo label kept", new CellEditAction("r", 0, "a", "b", "custom").Label == "custom");
    }
}
