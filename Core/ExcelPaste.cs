using System.Collections.Generic;
using System.Text;

namespace TableNote;

public static class ExcelPaste
{
	private readonly record struct ParseCoreResult(bool Ok, List<List<string>> Grid, string? Error);

	public static bool LooksLikeGrid(string text)
	{
		if (text.IndexOf('\t') < 0 && text.IndexOf('\n') < 0)
		{
			return text.IndexOf('\r') >= 0;
		}
		return true;
	}

	public static GridPasteResult Apply(NoteDocument doc, int startRow, int startCol, string text)
	{
		if (text.Length > 262144)
		{
			return GridPasteResult.Rejected(TableLimits.PasteTooBigMessage);
		}
		if (!LooksLikeGrid(text))
		{
			return GridPasteResult.NotAGrid();
		}
		ParseCoreResult parseCoreResult = ParseCore(text);
		if (!parseCoreResult.Ok)
		{
			return GridPasteResult.Rejected(parseCoreResult.Error ?? TableLimits.PasteTooBigMessage);
		}
		List<List<string>> grid = parseCoreResult.Grid;
		if (grid.Count == 0)
		{
			return GridPasteResult.NotAGrid();
		}
		if (startRow < 0)
		{
			startRow = 0;
		}
		if (startCol < 0)
		{
			startCol = 0;
		}
		int num = 0;
		foreach (List<string> item in grid)
		{
			if (item.Count > num)
			{
				num = item.Count;
			}
		}
		int num2 = startRow + grid.Count;
		int num3 = startCol + num;
		// P3: check the whole block BEFORE changing anything (rows, columns and content stay as they were).
		if (num2 > TableLimits.MaxRows)
		{
			return GridPasteResult.Rejected(Localizer.T("paste.too_many_rows", TableLimits.MaxRows));
		}
		// v2.3.1 (L6): a table that already has 31–32 columns (system status/due columns) may be pasted into
		// as long as no NEW column beyond the user limit is needed.
		if (num3 > System.Math.Max(doc.Columns.Count, NoteDocument.MaxUserColumns))
		{
			return GridPasteResult.Rejected(Localizer.T("paste.too_many_columns", NoteDocument.MaxUserColumns));
		}
		while (doc.Columns.Count < num3)
		{
			if (!doc.AddColumn())
			{
				return GridPasteResult.Rejected(Localizer.T("paste.too_many_columns", NoteDocument.MaxUserColumns));
			}
		}
		while (doc.Rows.Count < num2)
		{
			if (!doc.AppendRow())
			{
				return GridPasteResult.Rejected(Localizer.T("paste.too_many_rows", TableLimits.MaxRows));
			}
		}
		for (int i = 0; i < grid.Count; i++)
		{
			List<string> list = doc.Rows[startRow + i];
			List<string> list2 = grid[i];
			for (int j = 0; j < list2.Count; j++)
			{
				list[startCol + j] = list2[j];
			}
		}
		return GridPasteResult.Applied(grid.Count, num);
	}

	public static bool TryRead(string text, out List<List<string>> grid, out string? error)
	{
		ParseCoreResult parseCoreResult = ParseCore(text);
		grid = parseCoreResult.Grid;
		error = parseCoreResult.Error;
		return parseCoreResult.Ok;
	}

	/// <summary>v2.3.1 (L7): an unclosed quote (plain text like <c>"ด่วน</c> + new line) is re-read with quotes as
	/// ordinary characters instead of rejecting the whole paste.</summary>
	private static ParseCoreResult ParseCore(string text)
	{
		var r = ParseCore(text, literalQuotes: false);
		if (!r.Ok && r.Error == UnclosedQuote) return ParseCore(text, literalQuotes: true);
		return r;
	}

	/// <summary>v2.4.1: Excel only wraps a cell in quotes when it contains a line break / tab / quote AND then
	/// the closing quote is followed by a tab, a line break or the end. A plain cell such as <c>"ด่วน" งาน</c> or
	/// <c>"5" inch</c> is copied as-is and used to lose its quotes (→ <c>ด่วน งาน</c>). Unclosed → true (L7 fallback).</summary>
	private static bool IsQuotedField(string text, int open)
	{
		for (int j = open + 1; j < text.Length; j++)
		{
			if (text[j] != '"') continue;
			if (j + 1 < text.Length && text[j + 1] == '"') { j++; continue; }
			return j + 1 == text.Length || text[j + 1] is '\t' or '\r' or '\n';
		}
		return true;
	}

	private static string UnclosedQuote => TableLimits.BadQuoteMessage; // v2.4.2: localized, same call compares it

	private static ParseCoreResult ParseCore(string text, bool literalQuotes)
	{
		List<List<string>> grid = new List<List<string>>();
		if (text.Length > 262144)
		{
			return new ParseCoreResult(Ok: false, grid, TableLimits.PasteTooBigMessage);
		}
		List<string> row = new List<string>();
		StringBuilder cell = new StringBuilder();
		bool flag = false;
		bool started = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (flag)
			{
				switch (c)
				{
				case '"':
					if (i + 1 < text.Length && text[i + 1] == '"')
					{
						cell.Append('"');
						i++;
					}
					else
					{
						flag = false;
					}
					break;
				case '\r':
					cell.Append('\n');
					if (i + 1 < text.Length && text[i + 1] == '\n')
					{
						i++;
					}
					break;
				default:
					cell.Append(c);
					break;
				}
				started = true;
				continue;
			}
			if (c == '"' && cell.Length == 0 && !literalQuotes && IsQuotedField(text, i))
			{
				flag = true;
				started = true;
				continue;
			}
			switch (c)
			{
			case '\t':
			{
				ParseCoreResult? parseCoreResult = EndCell();
				if (parseCoreResult.HasValue)
				{
					return parseCoreResult.GetValueOrDefault();
				}
				started = true;
				break;
			}
			case '\n':
			case '\r':
			{
				if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
				{
					i++;
				}
				ParseCoreResult? parseCoreResult = EndRow();
				if (parseCoreResult.HasValue)
				{
					return parseCoreResult.GetValueOrDefault();
				}
				break;
			}
			default:
				cell.Append(c);
				started = true;
				break;
			}
		}
		if (flag)
		{
			return Fail(UnclosedQuote);
		}
		if (started || row.Count > 0 || cell.Length > 0)
		{
			ParseCoreResult? parseCoreResult = EndRow();
			if (parseCoreResult.HasValue)
			{
				return parseCoreResult.GetValueOrDefault();
			}
		}
		return new ParseCoreResult(Ok: true, grid, null);
		ParseCoreResult? EndCell()
		{
			if (cell.Length > TableLimits.MaxCellChars)
			{
				return Fail(Localizer.T("paste.cell_too_long", TableLimits.MaxCellChars));
			}
			if (row.Count >= NoteDocument.MaxUserColumns)
			{
				return Fail(Localizer.T("paste.too_many_columns", NoteDocument.MaxUserColumns));
			}
			row.Add(cell.ToString());
			cell.Clear();
			return null;
		}
		ParseCoreResult? EndRow()
		{
			ParseCoreResult? result = EndCell();
			if (result.HasValue)
			{
				return result;
			}
			if (grid.Count >= TableLimits.MaxRows)
			{
				return Fail(Localizer.T("paste.too_many_rows", TableLimits.MaxRows));
			}
			grid.Add(row);
			row = new List<string>();
			started = false;
			return null;
		}
		ParseCoreResult Fail(string message)
		{
			row.Clear();
			cell.Clear();
			return new ParseCoreResult(Ok: false, new List<List<string>>(), message);
		}
	}
}
