namespace TableNote;

public static class Recurrence
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string MonthlyPrefix = "monthly:";

    /// <summary>Kinds in dropdown order (language-neutral). Monthly is stored as "monthly:&lt;anchor day&gt;".</summary>
    public static readonly string[] Kinds = [None, Daily, Weekly, "monthly"];

    /// <summary>Labels in the CURRENT UI language, same order as <see cref="Kinds"/>.</summary>
    public static IReadOnlyList<string> Labels => Kinds.Select(k => Localizer.T("recur." + k)).ToList();

    /// <summary>Dropdown index for a stored value (0 = none).</summary>
    public static int IndexOf(string? value)
    {
        var k = NormalizeKind(value);
        if (k == Daily) return 1;
        if (k == Weekly) return 2;
        if (k.StartsWith(MonthlyPrefix, StringComparison.Ordinal)) return 3;
        return 0;
    }

    /// <summary>Stored code for a dropdown index; monthly takes its anchor from the due day.</summary>
    public static string FromIndex(int index, DateTime? dueForMonthlyAnchor = null) => index switch
    {
        1 => Daily,
        2 => Weekly,
        3 => MonthlyPrefix + (dueForMonthlyAnchor?.Day ?? DateTime.Now.Day),
        _ => None
    };

    public static bool IsActive(string? value)
    {
        var n = NormalizeKind(value);
        return n is Daily or Weekly || n.StartsWith(MonthlyPrefix, StringComparison.Ordinal);
    }

    /// <summary>"none" / "ไม่ซ้ำ" / blank (any case) — the key is removed on load.</summary>
    public static bool IsExplicitNone(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() is "" or "none" or "ไม่ซ้ำ" or "no repeat";

    public static string NormalizeKind(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v) || v == "none") return None;
        if (v is "no repeat" or "ไม่ซ้ำ" or "never") return None;
        if (v is "daily" or "วัน" or "ทุกวัน" or "every day") return Daily;
        if (v is "weekly" or "สัปดาห์" or "ทุกสัปดาห์" or "every week") return Weekly;
        if (v.StartsWith(MonthlyPrefix, StringComparison.Ordinal))
        {
            // monthly:<1..31> (0 = take day from due). Anything else (monthly:abc, monthly:45) = none.
            return int.TryParse(v[MonthlyPrefix.Length..], System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture, out var d) && d is >= 0 and <= 31
                ? MonthlyPrefix + d
                : None;
        }
        if (v is "monthly" or "เดือน" or "ทุกเดือน" or "every month") return MonthlyPrefix + "0";
        // Unknown values are treated as no repeat ("ไม่ซ้ำ")
        return None;
    }

    public static string Label(string? value) => Labels[IndexOf(value)];

    /// <summary>Compatibility: map a label in ANY supported language (or a code) to a stored value.</summary>
    public static string FromLabel(string? label, DateTime? dueForMonthlyAnchor = null)
    {
        var k = NormalizeKind(label);
        if (k.StartsWith(MonthlyPrefix, StringComparison.Ordinal))
            return k == MonthlyPrefix + "0" ? MonthlyPrefix + (dueForMonthlyAnchor?.Day ?? DateTime.Now.Day) : k;
        return k;
    }

    public static DateTime NextDue(DateTime baseDue, string kind, DateTime now)
    {
        kind = NormalizeKind(kind);
        DateTime next;
        if (kind == Daily)
            next = baseDue.AddDays(1);
        else if (kind == Weekly)
            next = baseDue.AddDays(7);
        else if (kind.StartsWith(MonthlyPrefix, StringComparison.Ordinal))
        {
            var anchor = 0;
            _ = int.TryParse(kind[MonthlyPrefix.Length..], out anchor);
            if (anchor <= 0) anchor = baseDue.Day;
            next = AddMonthsClamped(baseDue, 1, anchor);
        }
        else
            return baseDue;

        // v2.3.1: jump straight past "now" for daily/weekly (the 400-step guard left a task that was
        // overdue > 400 days/weeks with a next round in the PAST → an immediate "missed" reminder).
        if (next <= now && (kind == Daily || kind == Weekly))
        {
            var step = kind == Daily ? 1 : 7;
            var periods = (long)Math.Floor((now - next).TotalDays / step) + 1;
            next = next.AddDays(periods * step);
        }
        var guard = 0;
        while (next <= now && guard++ < 2400) // monthly: 200 years
        {
            if (kind == Daily) next = next.AddDays(1);
            else if (kind == Weekly) next = next.AddDays(7);
            else
            {
                var anchor = 0;
                _ = int.TryParse(kind[MonthlyPrefix.Length..], out anchor);
                if (anchor <= 0) anchor = baseDue.Day;
                next = AddMonthsClamped(next, 1, anchor);
            }
        }
        return next;
    }

    public static DateTime AddMonthsClamped(DateTime from, int months, int anchorDay)
    {
        var cursor = from.AddMonths(months);
        var last = DateTime.DaysInMonth(cursor.Year, cursor.Month);
        var day = Math.Min(anchorDay, last);
        return new DateTime(cursor.Year, cursor.Month, day, from.Hour, from.Minute, from.Second, from.Kind);
    }

    /// <summary>
    /// Mark row done and insert the next occurrence below. Returns status message or null.
    /// Excel paste must NOT call this.
    /// </summary>
    public static string? CompleteAndSpawnNext(NoteDocument doc, int row)
    {
        if (row < 0 || row >= doc.Rows.Count) return Localizer.T("recur.no_row");
        var id = doc.RowIdAt(row);
        var raw = doc.Recurrence.TryGetValue(id, out var r) ? r : None;
        var kind = NormalizeKind(raw);
        var statusCol = doc.StatusColumnIndex();
        var dueCol = doc.DueColumnIndex();

        // Always mark done on current row
        if (statusCol >= 0)
            doc.SetCellRaw(row, statusCol, TaskStatuses.DoneCode);
        ReminderPlan.OnDone(doc, id);

        if (!IsActive(kind))
            return null;

        if (dueCol < 0 || !DueDates.TryParse(doc.GetCell(row, dueCol), out var due))
            return Localizer.T("recur.bad_due");

        if (doc.Rows.Count >= TableLimits.MaxRows)
            return TableLimits.SpawnRowLimitMessage;

        // R4: keep the original anchor day (Jan 31 → Feb 28 → Mar 31). Only fill a missing (0) anchor.
        if (kind == MonthlyPrefix + "0")
            kind = MonthlyPrefix + due.Day;

        var nextDue = NextDue(due, kind, DateTime.Now);
        var newId = Guid.NewGuid().ToString("N");
        var clone = doc.Rows[row].ToList();
        if (statusCol >= 0 && statusCol < clone.Count)
            clone[statusCol] = TaskStatuses.TodoCode;
        if (dueCol >= 0 && dueCol < clone.Count)
            clone[dueCol] = DueDates.HasTime(doc.GetCell(row, dueCol)) ? DueDates.Format(nextDue) : DueDates.FormatDate(nextDue); // R3

        var insertAt = row + 1;
        doc.Rows.Insert(insertAt, clone);
        doc.EnsureRowIds();
        // RowIds was synced to old count; fix: EnsureRowIds appended at end — replace
        if (doc.RowIds.Count == doc.Rows.Count)
        {
            // last id is the auto one for new row at end — move to insertAt
            var auto = doc.RowIds[^1];
            doc.RowIds.RemoveAt(doc.RowIds.Count - 1);
            doc.RowIds.Insert(insertAt, newId);
            _ = auto;
        }
        else
        {
            doc.RowIds.Insert(insertAt, newId);
            doc.EnsureRowIds();
            doc.RowIds[insertAt] = newId;
        }

        // Move recurrence to new row; old keeps history without recurrence
        doc.Recurrence.Remove(id);
        doc.Recurrence[newId] = kind;
        // R9: next occurrence keeps advance-reminder offsets but not acknowledgements.
        if (doc.Reminders.TryGetValue(id, out var rem) && rem.Offsets.Count > 0)
        {
            doc.Reminders[newId] = new ReminderInfo { Offsets = rem.Offsets.ToList() };
            ReminderPlan.Reset(doc, newId, DateTime.Now);
        }
        return null;
    }
}
