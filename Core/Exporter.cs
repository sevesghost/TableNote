using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace TableNote;

public enum ExportFormat { Csv, Txt, Xlsx }

/// <summary>
/// One exported row, RAW as stored in note.json: cells in header order (status as code or typed text, due as typed)
/// and the row's recurrence code (none/daily/weekly/monthly:N, or null). <see cref="Exporter"/> turns codes into labels.
/// </summary>
public sealed record TaskRow(IReadOnlyList<string> Cells, string? Recurrence = null);

/// <summary>Which columns hold status / due (-1 = none). Default: detected from the header names.</summary>
public sealed record ExportColumns(int Status, int Due)
{
    public static ExportColumns Detect(IReadOnlyList<string> headers)
    {
        int Find(params string[] names)
        {
            for (var i = 0; i < headers.Count; i++)
                if (names.Contains((headers[i] ?? "").Trim(), StringComparer.OrdinalIgnoreCase)) return i;
            return -1;
        }
        return new ExportColumns(Find(NoteDocument.ColStatus, "Status"), Find(NoteDocument.ColDue, "Due", "Due date"));
    }
}

/// <summary>
/// Export to .csv (UTF-8 BOM, RFC 4180), .txt (tab-separated, UTF-8 BOM) or .xlsx (hand-written OpenXML).
/// Formula safety: .xlsx never contains &lt;f&gt; (text = inline strings; only plain decimals are numbers);
/// Dates: yyyy-MM-dd [HH:mm] CE text. .csv/.txt prefix ' to values starting with = + - @ TAB CR LF unless the whole value is a plain number.
/// Pure function of its inputs — safe to call off the UI thread.
/// </summary>
public static class Exporter
{
    private static readonly Regex CsvPlainNumber = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.CultureInvariant);
    // Numeric only if it round-trips exactly (no +, no trailing zeros, no -0, no leading zeros, no exponent).
    private static readonly Regex XlsxNumber = new(@"^-?(0|[1-9]\d*)(\.\d*[1-9])?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Writes <paramref name="rows"/> (raw) to <paramref name="path"/>. In the CURRENT UI language:
    /// status codes → labels, a blank status → the "Not started" label (as on screen), other status text as typed;
    /// due → yyyy-MM-dd, or yyyy-MM-dd HH:mm when the task has a time, when readable; a trailing Repeat column is added when any row repeats.
    /// Written to a temp file first, then moved over <paramref name="path"/>.
    /// </summary>
    public static void Export(IReadOnlyList<TaskRow> rows, IReadOnlyList<string> headers, string path, ExportFormat format, ExportColumns? columns = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(headers);
        (headers, rows) = Present(rows, headers, columns ?? ExportColumns.Detect(headers));
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                switch (format)
                {
                    case ExportFormat.Csv: WriteDelimited(fs, rows, headers, ',', CsvField); break;
                    case ExportFormat.Txt: WriteDelimited(fs, rows, headers, '\t', TxtField); break;
                    case ExportFormat.Xlsx: WriteXlsx(fs, rows, headers); break;
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public static ExportFormat FormatForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => ExportFormat.Csv,
        ".xlsx" => ExportFormat.Xlsx,
        _ => ExportFormat.Txt
    };

    /// <summary>Due date as CE text yyyy-MM-dd [HH:mm] (decision D9: never a Buddhist year, never an Excel serial date).</summary>
    public static string ExportDate(string raw, DateTime d) =>
        DueDates.HasTime(raw) || d.TimeOfDay != TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Display form of raw rows (what Export writes). Public for tests.</summary>
    public static (IReadOnlyList<string> Headers, IReadOnlyList<TaskRow> Rows) Present(IReadOnlyList<TaskRow> rows, IReadOnlyList<string> headers, ExportColumns cols)
    {
        var repeat = rows.Any(r => Recurrence.NormalizeKind(r.Recurrence) != Recurrence.None);
        var outHeaders = headers.ToList();
        if (repeat) outHeaders.Add(Localizer.T("export.repeat"));
        var outRows = new List<TaskRow>(rows.Count);
        foreach (var r in rows)
        {
            var cells = new List<string>(outHeaders.Count);
            for (var c = 0; c < headers.Count; c++)
            {
                var v = c < r.Cells.Count ? r.Cells[c] ?? "" : "";
                if (c == cols.Status)
                {
                    if (string.IsNullOrWhiteSpace(v)) v = TaskStatuses.Label(TaskStatuses.TodoCode);
                    else
                    {
                        var code = TaskStatuses.CanonicalOnLoad(v);
                        if (TaskStatuses.Codes.Contains(code)) v = TaskStatuses.Label(code);
                    }
                }
                else if (c == cols.Due && DueDates.TryParse(v, out var d))
                    v = ExportDate(v, d);
                cells.Add(v);
            }
            if (repeat)
            {
                var k = Recurrence.NormalizeKind(r.Recurrence);
                cells.Add(k == Recurrence.None ? "" : Recurrence.Label(k));
            }
            outRows.Add(new TaskRow(cells, r.Recurrence));
        }
        return (outHeaders, outRows);
    }

    /// <summary>
    /// RAW rows from a document for <see cref="Export"/>: all columns, all rows except trailing blank ones, hide-done respected,
    /// each row with its recurrence code.
    /// </summary>
    public static (List<string> Headers, List<TaskRow> Rows) FromDocument(NoteDocument doc, bool respectHideDone = true)
    {
        var headers = doc.Columns.ToList();
        var statusCol = doc.StatusColumnIndex();
        var rows = new List<TaskRow>();
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            if (respectHideDone && doc.HideDone && statusCol >= 0 && TaskStatuses.IsDone(doc.GetCell(r, statusCol))) continue;
            var cells = new string[headers.Count];
            for (var c = 0; c < headers.Count; c++)
                cells[c] = doc.GetCell(r, c);
            rows.Add(new TaskRow(cells, doc.Recurrence.TryGetValue(doc.RowIdAt(r), out var rec) ? rec : null));
        }
        // Blank rows in the middle stay (same row positions as on screen); trailing blank rows are dropped.
        while (rows.Count > 0 && rows[^1].Cells.All(string.IsNullOrEmpty)) rows.RemoveAt(rows.Count - 1);
        return (headers, rows);
    }

    // ---------- csv / txt ----------
    public static bool NeedsQuotePrefix(string v) =>
        v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' && !CsvPlainNumber.IsMatch(v);

    private static string Guard(string v) => NeedsQuotePrefix(v) ? "'" + v : v;

    public static string CsvField(string? value)
    {
        var v = Guard(value ?? "");
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 || v.StartsWith(' ') || v.EndsWith(' ')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    /// <summary>TSV (decision D4): no quoting; a tab or line break inside a cell becomes one space, so columns never shift.</summary>
    public static string TxtField(string? value)
    {
        var v = Guard(value ?? "");
        return v.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    private static void WriteDelimited(Stream s, IReadOnlyList<TaskRow> rows, IReadOnlyList<string> headers, char sep, Func<string?, string> field)
    {
        using var w = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1 << 16) { NewLine = "\r\n" };
        void Line(IEnumerable<string?> cells) => w.WriteLine(string.Join(sep, cells.Select(field)));
        Line(headers);
        foreach (var r in rows) Line(Enumerable.Range(0, headers.Count).Select(i => i < r.Cells.Count ? r.Cells[i] : ""));
    }

    // ---------- xlsx ----------
    public static bool IsXlsxNumber(string v) =>
        XlsxNumber.IsMatch(v) && v != "-0" && v.Count(char.IsDigit) <= 15 // >15 significant digits would be rounded by Excel
        // v2.4.1: 12+ digits before the point (13-digit Thai ID, account numbers) are shown by Excel's General
        // format as 1.10370E+12 and re-saved that way → keep them as text.
        && IntegerDigits(v) <= MaxXlsxIntegerDigits;

    private static int IntegerDigits(string v)
    {
        var dot = v.IndexOf('.');
        return (dot < 0 ? v.Length : dot) - (v.StartsWith('-') ? 1 : 0);
    }

    public const int MaxXlsxIntegerDigits = 11;

    private static string Xml(string v)
    {
        // A literal "_xHHHH_" in the text would be read back as an escape → escape its underscore first (E6).
        v = Regex.Replace(v, "_(?=x[0-9A-Fa-f]{4}_)", "_x005F_");
        var sb = new StringBuilder(v.Length + 8);
        for (var i = 0; i < v.Length; i++)
        {
            var ch = v[i];
            if (char.IsHighSurrogate(ch) && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1])) { sb.Append(ch).Append(v[++i]); continue; }
            if (char.IsSurrogate(ch)) { sb.Append('\uFFFD'); continue; }
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t': case '\n': case '\r': sb.Append(ch == '\r' ? "&#13;" : ch.ToString()); break;
                default:
                    if (ch < 0x20 || ch == '\uFFFE' || ch == '\uFFFF') sb.Append($"_x{(int)ch:X4}_"); // invalid in XML 1.0
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ColName(int index)
    {
        var s = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static void WriteXlsx(Stream s, IReadOnlyList<TaskRow> rows, IReadOnlyList<string> headers)
    {
        using var zip = new ZipArchive(s, ZipArchiveMode.Create, leaveOpen: true);
        void Entry(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        const string decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        Entry("[Content_Types].xml", decl +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");
        Entry("_rels/.rels", decl +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Entry("xl/workbook.xml", decl +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"TableNote\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels", decl +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");
        Entry("xl/styles.xml", decl +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Tahoma\"/></font><font><b/><sz val=\"11\"/><name val=\"Tahoma\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf></cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>");

        var e = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false), 1 << 16);
        w.Write(decl);
        w.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        w.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (headers.Count > 0)
        {
            w.Write("<cols>");
            for (var c = 0; c < headers.Count; c++)
            {
                var longest = Math.Max(headers[c].Length, rows.Count == 0 ? 0 : rows.Max(r => c < r.Cells.Count ? FirstLineLength(r.Cells[c]) : 0));
                var width = Math.Clamp(longest + 2, 8, 60);
                w.Write($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width}\" customWidth=\"1\"/>");
            }
            w.Write("</cols>");
        }
        w.Write("<sheetData>");
        void Row(int r, Func<int, string> get, int style)
        {
            w.Write($"<row r=\"{r}\">");
            for (var c = 0; c < headers.Count; c++)
            {
                var v = get(c) ?? "";
                if (v.Length == 0) continue;
                var refName = ColName(c) + r;
                var s = style != 0 ? $" s=\"{style}\"" : (v.Contains('\n') ? " s=\"2\"" : "");
                if (style == 0 && IsXlsxNumber(v))
                    w.Write($"<c r=\"{refName}\"{s}><v>{v}</v></c>");
                else
                    w.Write($"<c r=\"{refName}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xml(v)}</t></is></c>");
            }
            w.Write("</row>");
        }
        Row(1, c => headers[c], 1);
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Cells;
            Row(i + 2, c => c < cells.Count ? cells[c] : "", 0);
        }
        w.Write("</sheetData></worksheet>");
    }

    private static int FirstLineLength(string? v)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        var i = v.IndexOf('\n');
        return i < 0 ? v.Length : i;
    }
}
