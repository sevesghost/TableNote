using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TableNote;

public static class ExcelCopy
{
	public static bool AllowsClipboard(ViewMode mode)
	{
		return mode == ViewMode.Edit;
	}

	public static string? Selection(NoteDocument doc, int rowA, int colA, int rowB, int colB)
	{
		return Write(Slice(doc, rowA, colA, rowB, colB, includeHeaders: false), includeHeaders: false, null);
	}

	public static string? WholeTable(NoteDocument doc, bool includeHeaders)
	{
		if (doc.Rows.Count == 0 && !includeHeaders)
		{
			return "";
		}
		List<List<string>> list = new List<List<string>>(doc.Rows.Count);
		foreach (List<string> row in doc.Rows)
		{
			list.Add(row.ToList());
		}
		return Write(list, includeHeaders, doc.Columns);
	}

	public static string? Write(IReadOnlyList<IReadOnlyList<string>> rows, bool includeHeaders, IReadOnlyList<string>? headers)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (includeHeaders)
		{
			if (headers == null)
			{
				return null;
			}
			AppendRow(stringBuilder, headers);
		}
		foreach (IReadOnlyList<string> row in rows)
		{
			AppendRow(stringBuilder, row);
		}
		if (stringBuilder.Length > 262144)
		{
			return null;
		}
		return stringBuilder.ToString();
	}

	public static string Escape(string value)
	{
		value = value.Replace("\r\n", "\n").Replace('\r', '\n');
		if (value.IndexOf('\t') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('"') < 0)
		{
			return value;
		}
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}

	private static void AppendRow(StringBuilder sb, IReadOnlyList<string> cells)
	{
		for (int i = 0; i < cells.Count; i++)
		{
			if (i > 0)
			{
				sb.Append('\t');
			}
			sb.Append(Escape(cells[i] ?? ""));
		}
		sb.Append("\r\n");
	}

	private static List<List<string>> Slice(NoteDocument doc, int rowA, int colA, int rowB, int colB, bool includeHeaders)
	{
		List<List<string>> list = new List<List<string>>();
		if (doc.Rows.Count == 0 || doc.Columns.Count == 0)
		{
			return list;
		}
		int num = Math.Clamp(Math.Min(rowA, rowB), 0, doc.Rows.Count - 1);
		int num2 = Math.Clamp(Math.Max(rowA, rowB), 0, doc.Rows.Count - 1);
		int num3 = Math.Clamp(Math.Min(colA, colB), 0, doc.Columns.Count - 1);
		int num4 = Math.Clamp(Math.Max(colA, colB), 0, doc.Columns.Count - 1);
		if (includeHeaders)
		{
			List<string> list2 = new List<string>();
			for (int i = num3; i <= num4; i++)
			{
				list2.Add(doc.Columns[i]);
			}
			list.Add(list2);
		}
		for (int j = num; j <= num2; j++)
		{
			List<string> list3 = new List<string>();
			for (int k = num3; k <= num4; k++)
			{
				list3.Add((k < doc.Rows[j].Count) ? doc.Rows[j][k] : "");
			}
			list.Add(list3);
		}
		return list;
	}
}
