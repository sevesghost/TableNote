namespace TableNote;

/// <summary>
/// Delete and re-add the built-in status column and the reminder date column (day, time, repeat).
/// Each role exists at most once. A file that never recorded a removal still gets both columns.
/// </summary>
public readonly record struct ColumnAddChoice(string Role, bool Enabled, string? DisabledReasonKey);

public static class SystemColumnToggle
{
    public static bool IsStatus(string? role) =>
        string.Equals(role, ColumnRoles.Status, StringComparison.OrdinalIgnoreCase);

    public static bool IsDue(string? role) =>
        string.Equals(role, ColumnRoles.Due, StringComparison.OrdinalIgnoreCase);

    public static bool IsOmitted(NoteDocument doc, string? role)
    {
        if (IsStatus(role)) return doc.StatusColumnOmitted;
        if (IsDue(role)) return doc.DueColumnOmitted;
        return false;
    }

    public static void SetOmitted(NoteDocument doc, string role, bool omitted)
    {
        if (IsStatus(role)) doc.StatusColumnOmitted = omitted;
        else if (IsDue(role)) doc.DueColumnOmitted = omitted;
    }

    /// <summary>
    /// Header-name guesses stay on for every file that did not record a removal (including pre-2.1.0 notes with no columnRoles).
    /// They are suppressed only when that role's <c>ColumnOmitted</c> flag is true.
    /// </summary>
    public static bool AllowsNameInference(NoteDocument doc, string? role) => !IsOmitted(doc, role);

    /// <summary>Drop a status/due tag that was guessed onto another column after the user removed that role.</summary>
    public static void StripOmittedRoles(NoteDocument doc)
    {
        doc.EnsureColumnRoles();
        for (var i = 0; i < doc.ColumnRolesList.Count; i++)
            if (IsOmitted(doc, doc.ColumnRolesList[i])) doc.ColumnRolesList[i] = "";
    }

    /// <summary>
    /// Load-time repair. An omitted role stays missing. Otherwise a same-named column is adopted,
    /// or one is added, matching files that have no removal marker.
    /// </summary>
    public static void EnsurePresent(NoteDocument doc, string role, string fullWarningKey)
    {
        if (doc.IndexOfRole(role) >= 0)
        {
            // The column is really here, so a stale removal flag must not hide it.
            SetOmitted(doc, role, false);
            return;
        }
        if (IsOmitted(doc, role)) return;

        var named = doc.IndexOfDefaultName(role);
        if (named >= 0)
        {
            doc.EnsureColumnRoles();
            if (named < doc.ColumnRolesList.Count) doc.ColumnRolesList[named] = role;
            return;
        }
        if (!doc.AddColumn(NoteDocument.DefaultColumnName(role), role))
            doc.DueColumnWarning = Localizer.T(fullWarningKey);
        else
            doc.EnsureColumnRoles();
    }

    /// <summary>Header-menu and toolbar delete. Status and due may be removed; <see cref="DeletePlanner.ForColumn"/> still reports the old due-column block.</summary>
    public static DeletePlan PlanHeaderDelete(NoteDocument doc, int column)
    {
        if (column == doc.StatusColumnIndex() || column == doc.DueColumnIndex())
        {
            if (doc.Columns.Count <= 1)
                return new DeletePlan(false, false, Localizer.T("delete.min_one_column"), "");
            if (column < 0 || column >= doc.Columns.Count)
                return new DeletePlan(false, false, Localizer.T("delete.no_column"), "");
            var label = DeletePlanner.ColumnLabel(doc, column);
            if (!doc.ColumnHasContent(column))
                return new DeletePlan(true, true, "", Localizer.T("delete.column_confirm", label));
            return new DeletePlan(true, true, "", Localizer.T("delete.column_has_text", label));
        }
        return DeletePlanner.ForColumn(doc, column);
    }

    /// <summary>Choices inside the "+ Column" menu. A role that is already present is disabled.</summary>
    public static IReadOnlyList<ColumnAddChoice> AddChoices(NoteDocument doc)
    {
        doc.EnsureColumnRoles();
        return
        [
            new ColumnAddChoice("", true, null),
            Choice(doc, ColumnRoles.Status, "col.only_one_status"),
            Choice(doc, ColumnRoles.Due, "col.only_one_due")
        ];
    }

    private static ColumnAddChoice Choice(NoteDocument doc, string role, string reasonKey)
    {
        var exists = doc.IndexOfRole(role) >= 0;
        return new ColumnAddChoice(role, !exists, exists ? reasonKey : null);
    }

    /// <summary>Add a status or reminder column. Null when it was added; otherwise a message. The new cells are empty.</summary>
    public static string? Add(NoteDocument doc, string role)
    {
        if (IsStatus(role))
        {
            if (doc.IndexOfRole(ColumnRoles.Status) >= 0) return Localizer.T("col.only_one_status");
            if (!doc.AddColumn(NoteDocument.DefaultColumnName(ColumnRoles.Status), ColumnRoles.Status))
                return TableLimits.ColumnLimitMessage;
            SetOmitted(doc, ColumnRoles.Status, false);
            var idx = doc.IndexOfRole(ColumnRoles.Status);
            if (idx >= 0) doc.SetColumnWidth(idx, 120);
            return null;
        }
        if (IsDue(role))
        {
            if (doc.IndexOfRole(ColumnRoles.Due) >= 0) return Localizer.T("col.only_one_due");
            if (!doc.AddColumn(NoteDocument.DefaultColumnName(ColumnRoles.Due), ColumnRoles.Due))
                return TableLimits.ColumnLimitMessage;
            SetOmitted(doc, ColumnRoles.Due, false);
            ClearNotifyColors(doc); // a new reminder column must not keep the previous card colors
            var idx = doc.IndexOfRole(ColumnRoles.Due);
            if (idx >= 0) doc.SetColumnWidth(idx, 200);
            return null;
        }
        return Localizer.T("delete.no_column");
    }

    /// <summary>Drop header and cell colors stored for a column id so a later column cannot inherit them.</summary>
    public static void ForgetColumnColors(NoteDocument doc, string columnId)
    {
        if (doc.PartColors == null || doc.PartColors.Count == 0 || string.IsNullOrEmpty(columnId)) return;
        doc.PartColors.Remove(PartColorKeys.HeaderColumn(columnId));
        var suffix = ":" + columnId;
        foreach (var key in doc.PartColors.Keys.Where(k =>
                     k.StartsWith("cell:", StringComparison.Ordinal) &&
                     k.EndsWith(suffix, StringComparison.Ordinal)).ToList())
            doc.PartColors.Remove(key);
    }

    /// <summary>Reminder date column is gone: repeats, pending reminders, and card colors go with it.</summary>
    public static void ClearDueSideEffects(NoteDocument doc)
    {
        doc.Recurrence.Clear();
        doc.Reminders.Clear();
        doc.Snoozes.Clear();
        doc.Acknowledged.Clear();
        ClearNotifyColors(doc);
    }

    public static void ClearNotifyColors(NoteDocument doc)
    {
        if (doc.PartColors == null || doc.PartColors.Count == 0) return;
        foreach (var key in doc.PartColors.Keys.Where(k => k.StartsWith("notify:", StringComparison.Ordinal)).ToList())
            doc.PartColors.Remove(key);
    }
}
