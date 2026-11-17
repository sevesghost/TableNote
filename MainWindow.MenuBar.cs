using Avalonia.Controls;

namespace TableNote;

/// <summary>
/// Top menu bar. Column, row, and detail items are the same objects the context menus build
/// (<see cref="BuildColumnMenuItems"/>, <see cref="CreateRowMenuItems"/>, <see cref="BuildDetailButtonMenuItems"/>),
/// aimed at the selected cell. They are disabled when that target is missing.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<int, TextBox> _menuHeaderBoxes = new();
    private readonly List<Action> _menuTextRefresh = [];

    private MenuItem _fileMenu = null!;
    private MenuItem _tableMenu = null!;
    private MenuItem _columnMenu = null!;
    private MenuItem _rowMenu = null!;
    private MenuItem _viewMenu = null!;
    private MenuItem _helpMenu = null!;
    private MenuItem _systemMenu = null!;
    private MenuItem _languageMenu = null!;

    private MenuItem _copyAllItem = null!;
    private MenuItem _copyHeadersItem = null!;
    private MenuItem _exportItem = null!;
    private MenuItem _exportImageItem = null!;
    private MenuItem _printItem = null!;
    private MenuItem _pinItem = null!;
    private MenuItem _colorItem = null!;
    private MenuItem _addRowItem = null!;
    private MenuItem _deleteRowItem = null!;
    private MenuItem _addColumnItem = null!;
    private MenuItem _deleteColumnItem = null!;
    private MenuItem _sortItem = null!;
    private MenuItem _viewSortItem = null!;
    private MenuItem _systemPinItem = null!;
    private MenuItem _hideDoneItem = null!;
    private MenuItem _zoomOutItem = null!;
    private MenuItem _zoomResetItem = null!;
    private MenuItem _zoomInItem = null!;
    private MenuItem _topmostItem = null!;
    private MenuItem _lockItem = null!;
    private MenuItem _launchItem = null!;
    private MenuItem _muteItem = null!;
    private MenuItem _helpItem = null!;
    private MenuItem _logItem = null!;

    // SPOT: team ตก — add the alert-date column and the Status column (max 1 each).
    // Enabled only when that column is missing. Do not move these items.
    private MenuItem _spotAddDueItem = null!;
    private MenuItem _spotAddStatusItem = null!;

    private void RememberMenuHeaderBox(int column, TextBox box) => _menuHeaderBoxes[column] = box;

    private void ClearMenuHeaderBoxes() => _menuHeaderBoxes.Clear();

    private void InitMenuBar()
    {
        MainMenu.FontFamily = AppFonts.Thai;

        _fileMenu = new MenuItem();
        _copyAllItem = Act("toolbar.copy_all", () => _ = CopyAsync(true, false));
        _copyHeadersItem = Act("toolbar.copy_headers", () => _ = CopyAsync(true, true));
        _exportItem = Act("toolbar.export", () => _ = ExportAsync());
        _exportImageItem = Act("toolbar.export_image", () => _ = ExportImageAsync());
        _printItem = Act("toolbar.print", () => _ = PrintViewAsync());
        _pinItem = Act("toolbar.background", OnBackgroundClick);
        _fileMenu.Items.Add(_copyAllItem);
        _fileMenu.Items.Add(_copyHeadersItem);
        _fileMenu.Items.Add(_exportItem);
        _fileMenu.Items.Add(_exportImageItem);
        _fileMenu.Items.Add(_printItem);
        _fileMenu.Items.Add(new Separator());
        _fileMenu.Items.Add(_pinItem);
        _fileMenu.SubmenuOpened += (_, _) =>
        {
            var edit = _view == ViewMode.Edit;
            _copyAllItem.IsEnabled = edit;
            _copyHeadersItem.IsEnabled = edit;
            _exportItem.IsEnabled = edit && !_exporting;
            _exportImageItem.IsEnabled = edit && !_imageBusy;
            _printItem.IsEnabled = edit && !_imageBusy;
            _pinItem.IsEnabled = edit;
        };

        _tableMenu = new MenuItem();
        _colorItem = Act("color.button", () => _ = OpenColorDialog());
        _addRowItem = Act("toolbar.add_row", () => _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_row"), AddRowAsync)));
        _deleteRowItem = Act("toolbar.delete_row", () => _ = RunStructureAsync(DeleteRowAsync));
        _addColumnItem = Act("toolbar.add_column", () => _ = RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), AddColumnAsync)));
        _deleteColumnItem = Act("toolbar.delete_column", () => _ = RunStructureAsync(() => SnapshotStep(Localizer.T("delete.undo_column"), DeleteColumnAsync)));
        _sortItem = Act("toolbar.sort_due", SortByDue);
        _spotAddDueItem = new MenuItem { Tag = "spot:add-alert-date" };
        _spotAddDueItem.Click += async (_, _) =>
            await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), () => AddSystemColumnAsync(ColumnRoles.Due)));
        _spotAddStatusItem = new MenuItem { Tag = "spot:add-status" };
        _spotAddStatusItem.Click += async (_, _) =>
            await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), () => AddSystemColumnAsync(ColumnRoles.Status)));
        _tableMenu.Items.Add(_colorItem);
        _tableMenu.Items.Add(_addRowItem);
        _tableMenu.Items.Add(_deleteRowItem);
        _tableMenu.Items.Add(_addColumnItem);
        _tableMenu.Items.Add(_deleteColumnItem);
        _tableMenu.Items.Add(_sortItem);
        _tableMenu.Items.Add(new Separator());
        _tableMenu.Items.Add(_spotAddDueItem);
        _tableMenu.Items.Add(_spotAddStatusItem);
        _tableMenu.SubmenuOpened += (_, _) =>
        {
            var edit = _view == ViewMode.Edit && !_busy;
            _colorItem.IsEnabled = _view == ViewMode.Edit;
            _addRowItem.IsEnabled = edit;
            _deleteRowItem.IsEnabled = edit;
            _addColumnItem.IsEnabled = edit;
            _deleteColumnItem.IsEnabled = edit;
            _sortItem.IsEnabled = _view == ViewMode.Edit;
            ApplySpecialColumnSpots();
        };

        _columnMenu = new MenuItem();
        _columnMenu.SubmenuOpened += (_, _) => RefreshColumnMenu();

        _rowMenu = new MenuItem();
        _rowMenu.SubmenuOpened += (_, _) => RefreshRowMenu();

        _hideDoneItem = CheckItem("toolbar.hide_done", HideDoneBox);
        _viewSortItem = Act("toolbar.sort_due", SortByDue);
        _zoomOutItem = Act("tray.zoom_out", () => SetZoom(_settings.Scale - AppSettings.ScaleStep));
        _zoomResetItem = Act("tray.zoom_reset", () => SetZoom(100));
        _zoomInItem = Act("tray.zoom_in", () => SetZoom(_settings.Scale + AppSettings.ScaleStep));
        _languageMenu = new MenuItem();
        _languageMenu.SubmenuOpened += (_, _) => FillLanguageMenu();

        _systemMenu = new MenuItem();
        _topmostItem = CheckItem("toolbar.topmost", TopmostBox);
        _lockItem = CheckItem("toolbar.lock", LockBox);
        _launchItem = CheckItem("toolbar.launch_at_login", LaunchBox);
        _muteItem = CheckItem("toolbar.mute", MuteSoundBox);
        _systemPinItem = Act("toolbar.background", OnBackgroundClick);
        _systemMenu.Items.Add(_topmostItem);
        _systemMenu.Items.Add(_lockItem);
        _systemMenu.Items.Add(_launchItem);
        _systemMenu.Items.Add(_muteItem);
        _systemMenu.Items.Add(_systemPinItem);
        _systemMenu.SubmenuOpened += (_, _) =>
        {
            RefreshChecks();
            _systemPinItem.IsEnabled = _view == ViewMode.Edit;
        };

        _viewMenu = new MenuItem();
        _viewMenu.Items.Add(_hideDoneItem);
        _viewMenu.Items.Add(_viewSortItem);
        _viewMenu.Items.Add(new Separator());
        _viewMenu.Items.Add(_zoomOutItem);
        _viewMenu.Items.Add(_zoomResetItem);
        _viewMenu.Items.Add(_zoomInItem);
        _viewMenu.Items.Add(_languageMenu);
        _viewMenu.Items.Add(new Separator());
        _viewMenu.Items.Add(_systemMenu);
        _viewMenu.SubmenuOpened += (_, _) =>
        {
            var edit = _view == ViewMode.Edit;
            _hideDoneItem.IsEnabled = edit;
            _viewSortItem.IsEnabled = edit;
            _zoomOutItem.IsEnabled = edit;
            _zoomResetItem.IsEnabled = edit;
            _zoomInItem.IsEnabled = edit;
            RefreshChecks();
        };

        _helpItem = Act("tray.help", ShowHelp);
        _logItem = Act("tray.log", ShowLog);
        _helpMenu = new MenuItem();
        _helpMenu.Items.Add(_helpItem);
        _helpMenu.Items.Add(_logItem);

        MainMenu.Items.Add(_fileMenu);
        MainMenu.Items.Add(_tableMenu);
        MainMenu.Items.Add(_columnMenu);
        MainMenu.Items.Add(_rowMenu);
        MainMenu.Items.Add(_viewMenu);
        MainMenu.Items.Add(_helpMenu);

        ApplyMenuBarTexts();
        RefreshColumnMenu();
        RefreshRowMenu();
        FillLanguageMenu();
    }

    /// <summary>Column menu for the selected column. No selection lists the plain column commands, disabled.</summary>
    private void RefreshColumnMenu()
    {
        _columnMenu.Items.Clear();
        var column = MenuColumnIndex();
        TextBox? box = null;
        if (column >= 0) _menuHeaderBoxes.TryGetValue(column, out box);
        foreach (var item in BuildColumnMenuItems(column, box, keepClipboardSlot: box == null))
            _columnMenu.Items.Add(item);
    }

    /// <summary>Row menu for the selected row, plus Open / Hide from the detail button.</summary>
    private void RefreshRowMenu()
    {
        NoteSizingSelection();
        _rowMenu.Items.Clear();
        foreach (var item in CreateRowMenuItems(MenuRowIndex()))
            _rowMenu.Items.Add(item);
        _rowMenu.Items.Add(new Separator());
        MenuCell(out var row, out var column);
        foreach (var item in BuildDetailButtonMenuItems(row, column))
            _rowMenu.Items.Add(item);
    }

    private int MenuColumnIndex()
    {
        if (_view != ViewMode.Edit || !_session.HasSelection) return -1;
        var column = _session.ActiveCol;
        return column >= 0 && column < _session.Doc.Columns.Count ? column : -1;
    }

    private int MenuRowIndex()
    {
        if (_view != ViewMode.Edit || !_session.HasSelection) return -1;
        var row = _session.ActiveRow;
        return row >= 0 && row < _session.Doc.Rows.Count ? row : -1;
    }

    private void MenuCell(out int row, out int column)
    {
        row = _session.HasSelection ? _session.ActiveRow : -1;
        column = _session.HasSelection ? _session.ActiveCol : -1;
        if (row < 0 || column < 0 || row >= _session.Doc.Rows.Count || column >= _session.Doc.Columns.Count)
        {
            row = -1;
            column = -1;
        }
    }

    private void FillLanguageMenu()
    {
        _languageMenu.Items.Clear();
        foreach (var code in Localizer.Supported)
        {
            var c = code;
            var item = new MenuItem
            {
                Header = Localizer.LanguageName(c),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = Localizer.Current == c
            };
            item.Click += (_, _) => SwitchLanguage(c);
            _languageMenu.Items.Add(item);
        }
    }

    private void ApplyMenuBarTexts()
    {
        _fileMenu.Header = Localizer.T("menu.file");
        _tableMenu.Header = Localizer.T("menu.table");
        _columnMenu.Header = Localizer.T("menu.column");
        _rowMenu.Header = Localizer.T("menu.row");
        _viewMenu.Header = Localizer.T("menu.view");
        _helpMenu.Header = Localizer.T("menu.help");
        _systemMenu.Header = Localizer.T("menu.system");
        _languageMenu.Header = Localizer.T("menu.language");

        GroupTableLabel.Text = Localizer.T("menu.group.table");
        GroupCopyLabel.Text = Localizer.T("menu.group.copy");
        GroupViewLabel.Text = Localizer.T("menu.group.view");
        GroupSystemLabel.Text = Localizer.T("menu.group.system");
        GroupHelpLabel.Text = Localizer.T("menu.group.help");

        _copyAllItem.Header = Localizer.T("toolbar.copy_all");
        _copyHeadersItem.Header = Localizer.T("toolbar.copy_headers");
        _exportItem.Header = Localizer.T("toolbar.export");
        _exportImageItem.Header = Localizer.T("toolbar.export_image");
        _printItem.Header = Localizer.T("toolbar.print");
        _pinItem.Header = Localizer.T("toolbar.background");
        _colorItem.Header = Localizer.T("color.button");
        _addRowItem.Header = Localizer.T("toolbar.add_row");
        _deleteRowItem.Header = Localizer.T("toolbar.delete_row");
        _addColumnItem.Header = Localizer.T("toolbar.add_column");
        _deleteColumnItem.Header = Localizer.T("toolbar.delete_column");
        _sortItem.Header = Localizer.T("toolbar.sort_due");
        _viewSortItem.Header = Localizer.T("toolbar.sort_due");
        _systemPinItem.Header = Localizer.T("toolbar.background");
        _helpItem.Header = Localizer.T("tray.help");
        _logItem.Header = Localizer.T("tray.log");
        _zoomOutItem.Header = Localizer.T("tray.zoom_out");
        _zoomResetItem.Header = Localizer.T("tray.zoom_reset");
        _zoomInItem.Header = Localizer.T("tray.zoom_in");

        _spotAddDueItem.Header = Localizer.T("menu.spot.add_due");
        _spotAddStatusItem.Header = Localizer.T("menu.spot.add_status");
        ApplySpecialColumnSpots();

        foreach (var refresh in _menuTextRefresh) refresh();
        FillLanguageMenu();
        if (_columnMenu.Items.Count > 0) RefreshColumnMenu();
        if (_rowMenu.Items.Count > 0) RefreshRowMenu();
    }

    private MenuItem Act(string key, Action act)
    {
        var item = new MenuItem { Header = Localizer.T(key) };
        item.Click += (_, _) => act();
        return item;
    }

    private MenuItem CheckItem(string key, CheckBox box)
    {
        var item = new MenuItem();
        void Refresh() => item.Header = (box.IsChecked == true ? "✓ " : "") + Localizer.T(key);
        Refresh();
        item.Click += (_, _) =>
        {
            box.IsChecked = box.IsChecked != true;
            Refresh();
        };
        _menuTextRefresh.Add(Refresh);
        return item;
    }

    private void RefreshChecks()
    {
        foreach (var refresh in _menuTextRefresh) refresh();
    }

    /// <summary>Reserved table-menu slots. Disabled in view mode, and while that column already exists.</summary>
    private void ApplySpecialColumnSpots()
    {
        var doc = _session.Doc;
        var edit = _view == ViewMode.Edit && !_busy;
        _spotAddDueItem.IsEnabled = edit && MenuCommandRules.AddDueColumn(doc);
        _spotAddStatusItem.IsEnabled = edit && MenuCommandRules.AddStatusColumn(doc);
        ToolTip.SetTip(_spotAddDueItem, MenuCommandRules.AddDueColumn(doc) ? null : Localizer.T("col.only_one_due"));
        ToolTip.SetTip(_spotAddStatusItem, MenuCommandRules.AddStatusColumn(doc) ? null : Localizer.T("col.only_one_status"));
    }
}
