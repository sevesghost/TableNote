using Avalonia.Controls;

namespace TableNote;

/// <summary>The "+ Column" menu: a normal column, or the status / reminder date column when it is missing.</summary>
public partial class MainWindow
{
    private void InitColumnToggle()
    {
        AddColumnButton.Click += (_, _) => ShowAddColumnMenu();
    }

    private void ShowAddColumnMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var choice in SystemColumnToggle.AddChoices(_session.Doc))
        {
            var role = choice.Role;
            var item = new MenuItem
            {
                Header = AddChoiceHeader(choice),
                IsEnabled = choice.Enabled
            };
            if (!choice.Enabled && choice.DisabledReasonKey != null)
                ToolTip.SetTip(item, DisabledReason(role));
            item.Click += async (_, _) =>
            {
                if (role.Length == 0)
                    await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), AddColumnAsync));
                else
                    await RunStructureAsync(() => SnapshotStep(Localizer.T("undo.add_column"), () => AddSystemColumnAsync(role)));
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(AddColumnButton);
    }

    private static string AddChoiceHeader(ColumnAddChoice choice)
    {
        var label = choice.Role switch
        {
            ColumnRoles.Status => Localizer.T("col.add_status"),
            ColumnRoles.Due => Localizer.T("col.add_due"),
            _ => Localizer.T("col.add_plain")
        };
        if (choice.Enabled || choice.DisabledReasonKey == null) return label;
        return label + " — " + DisabledReason(choice.Role);
    }

    private static string DisabledReason(string role) =>
        role == ColumnRoles.Status ? Localizer.T("col.only_one_status") : Localizer.T("col.only_one_due");

    private Task AddSystemColumnAsync(string role)
    {
        if (_view != ViewMode.Edit) return Task.CompletedTask;
        var err = _session.AddSystemColumn(role);
        if (err != null) { StatusText.Text = err; return Task.CompletedTask; }
        Rebuild();
        FocusActiveCell();
        FlushSave();
        try { _reminders.CheckNow(); }
        catch (Exception ex) { AppLog.Write("reminders", "check after column add: " + ex.Message); }
        StatusText.Text = role == ColumnRoles.Status
            ? Localizer.T("col.added_status")
            : Localizer.T("col.added_due");
        return Task.CompletedTask;
    }

    /// <summary>After a column is removed: refresh reminders, and warn only when the due column vanished unexpectedly.</summary>
    private void AfterColumnRemoved()
    {
        try { _reminders.CheckNow(); }
        catch (Exception ex) { AppLog.Write("reminders", "check after column delete: " + ex.Message); }
        if (_session.Doc.DueColumnIndex() < 0 && !_session.Doc.DueColumnOmitted)
            ShowNotice(Localizer.T("ui.no_due_column"));
    }
}

public sealed partial class TableSession
{
    /// <summary>Re-add the status or reminder date column. Cells start empty. A second copy is refused.</summary>
    public string? AddSystemColumn(string role)
    {
        var err = SystemColumnToggle.Add(Doc, role);
        if (err != null) return err;
        ActiveCol = Doc.Columns.Count - 1;
        if (Doc.Rows.Count > 0 && ActiveRow < 0) ActiveRow = 0;
        HasSelection = true;
        return null;
    }
}
