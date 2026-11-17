namespace TableNote;

/// <summary>
/// v2.1 collapsible columns. The table stays a flat list (Columns / ColumnIds / rows). A group is metadata:
/// a parent column id plus ordered child column ids, and whether the children are shown.
/// Collapsing only hides children in the grid — their cells stay in the document and in note.json.
/// Older files have no "columnGroups" field and load with an empty list (no migration, no version bump).
/// </summary>
public sealed class ColumnGroup
{
    /// <summary>Stable id of the parent column (the one that stays visible, with the ▸/▾ toggle).</summary>
    public string ParentId { get; set; } = "";
    /// <summary>Stable ids of the child columns, in group order. They remain in the document when collapsed.</summary>
    public List<string> ChildIds { get; set; } = [];
    /// <summary>True: children are shown. False: only the parent column is shown. Missing on load means shown.</summary>
    public bool Expanded { get; set; } = true;
}

/// <summary>Result of a group edit. <see cref="Error"/> is already localized.</summary>
public readonly record struct GroupEdit(bool Ok, string? Error)
{
    public static GroupEdit Done() => new(true, null);
    public static GroupEdit Fail(string error) => new(false, error);
}

public static class ColumnGroupOps
{
    public const int DefaultChildCount = 3;
    public const int MinChildCount = 1;
    /// <summary>Cap on children of one group (the table's own column cap still applies).</summary>
    public const int MaxChildCount = 12;

    public static List<ColumnGroup> Clone(List<ColumnGroup>? groups) =>
        (groups ?? []).Where(g => g != null).Select(g => new ColumnGroup
        {
            ParentId = g.ParentId ?? "",
            ChildIds = (g.ChildIds ?? []).Where(id => id != null).Select(id => id!).ToList(),
            Expanded = g.Expanded
        }).ToList();

    public static ColumnGroup? GroupForParent(NoteDocument doc, int column)
    {
        if (column < 0 || column >= doc.Columns.Count) return null;
        doc.EnsureColumnIds();
        var id = doc.ColumnIds[column];
        return (doc.ColumnGroups ?? []).FirstOrDefault(g => g != null && string.Equals(g.ParentId, id, StringComparison.Ordinal));
    }

    public static ColumnGroup? GroupForChild(NoteDocument doc, int column)
    {
        if (column < 0 || column >= doc.Columns.Count) return null;
        doc.EnsureColumnIds();
        var id = doc.ColumnIds[column];
        return (doc.ColumnGroups ?? []).FirstOrDefault(g => g != null && g.ChildIds != null && g.ChildIds.Contains(id));
    }

    public static bool IsParent(NoteDocument doc, int column) => GroupForParent(doc, column) != null;
    public static bool IsChild(NoteDocument doc, int column) => GroupForChild(doc, column) != null;

    /// <summary>A child of a collapsed group. The parent itself is never hidden. Data is not touched.</summary>
    public static bool IsHidden(NoteDocument doc, int column)
    {
        var g = GroupForChild(doc, column);
        return g != null && !g.Expanded;
    }

    public static int VisibleCount(NoteDocument doc) =>
        Enumerable.Range(0, doc.Columns.Count).Count(i => !IsHidden(doc, i));

    /// <summary>Indices of the parent then each existing child, in group order.</summary>
    internal static List<int> MemberIndices(NoteDocument doc, ColumnGroup group)
    {
        doc.EnsureColumnIds();
        var list = new List<int>();
        var p = doc.ColumnIds.IndexOf(group.ParentId ?? "");
        if (p >= 0) list.Add(p);
        foreach (var id in group.ChildIds ?? [])
        {
            if (string.IsNullOrEmpty(id)) continue;
            var i = doc.ColumnIds.IndexOf(id);
            if (i >= 0 && !list.Contains(i)) list.Add(i);
        }
        return list;
    }

    /// <summary>Turn <paramref name="parent"/> into a group and insert <paramref name="childCount"/> child columns after it.</summary>
    public static GroupEdit Create(NoteDocument doc, int parent, int childCount)
    {
        doc.EnsureColumnIds();
        doc.EnsureColumnRoles();
        doc.ColumnGroups ??= [];
        if (parent < 0 || parent >= doc.Columns.Count) return GroupEdit.Fail(Localizer.T("colgroup.not_column"));
        if (childCount < MinChildCount || childCount > MaxChildCount)
            return GroupEdit.Fail(Localizer.T("colgroup.bad_count", MinChildCount, MaxChildCount));
        var parentId = doc.ColumnIdAt(parent);
        if (IsParent(doc, parent) || IsChild(doc, parent)) return GroupEdit.Fail(Localizer.T("colgroup.already"));
        if (doc.Columns.Count + childCount > NoteDocument.MaxUserColumns)
            return GroupEdit.Fail(Localizer.T("colgroup.no_room", childCount, NoteDocument.MaxUserColumns));

        var parentName = doc.Columns[parent];
        var added = new List<string>();
        for (var i = 1; i <= childCount; i++)
        {
            var name = UniqueName(doc, Localizer.T("colgroup.child_name", parentName, i));
            if (!doc.AddColumn(name, ""))
            {
                RollbackAdded(doc, added);
                return GroupEdit.Fail(Localizer.T("colgroup.no_room", childCount, NoteDocument.MaxUserColumns));
            }
            var newIndex = doc.Columns.Count - 1;
            var newId = doc.ColumnIdAt(newIndex);
            var dest = parent + i;
            if (newIndex != dest) doc.MoveColumn(newIndex, dest);
            added.Add(newId);
        }
        doc.ColumnGroups.Add(new ColumnGroup { ParentId = parentId, ChildIds = added, Expanded = true });
        return GroupEdit.Done();
    }

    public static GroupEdit AddChild(NoteDocument doc, int parent)
    {
        doc.EnsureColumnIds();
        var group = GroupForParent(doc, parent);
        if (group == null) return GroupEdit.Fail(Localizer.T("colgroup.not_group"));
        if (group.ChildIds.Count >= MaxChildCount)
            return GroupEdit.Fail(Localizer.T("colgroup.bad_count", MinChildCount, MaxChildCount));
        if (doc.Columns.Count + 1 > NoteDocument.MaxUserColumns)
            return GroupEdit.Fail(Localizer.T("colgroup.no_room", 1, NoteDocument.MaxUserColumns));
        var name = UniqueName(doc, Localizer.T("colgroup.child_name", doc.Columns[parent], group.ChildIds.Count + 1));
        if (!doc.AddColumn(name, ""))
            return GroupEdit.Fail(Localizer.T("colgroup.no_room", 1, NoteDocument.MaxUserColumns));
        var newIndex = doc.Columns.Count - 1;
        var newId = doc.ColumnIdAt(newIndex);
        var dest = MemberIndices(doc, group).DefaultIfEmpty(parent).Max() + 1;
        group.ChildIds.Add(newId);
        if (dest != newIndex) doc.MoveColumn(newIndex, dest);
        return GroupEdit.Done();
    }

    public static GroupEdit RenameChildren(NoteDocument doc, int parent, IReadOnlyList<string?> names)
    {
        var group = GroupForParent(doc, parent);
        if (group == null) return GroupEdit.Fail(Localizer.T("colgroup.not_group"));
        for (var i = 0; i < group.ChildIds.Count && i < names.Count; i++)
        {
            var idx = doc.ColumnIds.IndexOf(group.ChildIds[i]);
            if (idx < 0) continue;
            doc.Columns[idx] = TableLimits.Clip(names[i] ?? "", TableLimits.MaxCellChars);
        }
        return GroupEdit.Done();
    }

    /// <summary>Drop the group definition. Every column and every cell stays.</summary>
    public static GroupEdit Ungroup(NoteDocument doc, int parent)
    {
        var group = GroupForParent(doc, parent);
        if (group == null) return GroupEdit.Fail(Localizer.T("colgroup.not_group"));
        doc.ColumnGroups.Remove(group);
        return GroupEdit.Done();
    }

    public static GroupEdit Toggle(NoteDocument doc, int parent)
    {
        var group = GroupForParent(doc, parent);
        if (group == null) return GroupEdit.Fail(Localizer.T("colgroup.not_group"));
        group.Expanded = !group.Expanded;
        return GroupEdit.Done();
    }

    public static GroupEdit SetExpanded(NoteDocument doc, int parent, bool expanded)
    {
        var group = GroupForParent(doc, parent);
        if (group == null) return GroupEdit.Fail(Localizer.T("colgroup.not_group"));
        group.Expanded = expanded;
        return GroupEdit.Done();
    }

    /// <summary>
    /// Drop groups whose parent is gone, child ids that don't exist, and a column that would be in two groups.
    /// An empty group is removed. Cell text is never changed.
    /// </summary>
    public static void Sanitize(NoteDocument doc)
    {
        doc.ColumnGroups ??= [];
        doc.EnsureColumnIds();
        var ids = new HashSet<string>(doc.ColumnIds, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var keep = new List<ColumnGroup>();
        foreach (var g in doc.ColumnGroups)
        {
            if (g == null) continue;
            var parent = (g.ParentId ?? "").Trim();
            if (parent.Length == 0 || !ids.Contains(parent) || !used.Add(parent)) continue;
            var children = new List<string>();
            foreach (var raw in g.ChildIds ?? [])
            {
                var id = (raw ?? "").Trim();
                if (id.Length == 0 || id == parent || !ids.Contains(id) || !used.Add(id)) continue;
                children.Add(id);
            }
            if (children.Count == 0)
            {
                used.Remove(parent);
                continue;
            }
            g.ParentId = parent;
            g.ChildIds = children;
            keep.Add(g);
        }
        doc.ColumnGroups = keep;
    }

    private static string UniqueName(NoteDocument doc, string want)
    {
        want = TableLimits.Clip((want ?? "").Trim(), TableLimits.MaxCellChars);
        if (want.Length == 0) want = doc.NextColumnName();
        if (!doc.Columns.Contains(want)) return want;
        for (var n = 2; n < 10000; n++)
        {
            var suffix = " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var room = Math.Max(1, TableLimits.MaxCellChars - suffix.Length);
            var candidate = TableLimits.Clip(want, room) + suffix;
            if (!doc.Columns.Contains(candidate)) return candidate;
        }
        return doc.NextColumnName();
    }

    private static void RollbackAdded(NoteDocument doc, List<string> ids)
    {
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            var idx = doc.ColumnIds.IndexOf(ids[i]);
            if (idx >= 0) doc.RemoveColumn(idx);
        }
    }
}
