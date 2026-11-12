using System;
using System.Collections.Generic;

namespace TableNote;

public sealed class MemoryStartupRegistration : IStartupRegistration
{
	private readonly Dictionary<string, string> _entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyDictionary<string, string> Entries => _entries;

	public int Writes { get; private set; }

	public StartupSyncResult Apply(bool enabled, string command)
	{
		string a = (_entries.TryGetValue("TableNote", out string value) ? value : null);
		StartupSyncResult result = StartupLaunch.SyncEntry(_entries, enabled, command);
		string b = (_entries.TryGetValue("TableNote", out string value2) ? value2 : null);
		if (!string.Equals(a, b, StringComparison.Ordinal))
		{
			Writes++;
		}
		return result;
	}
}
