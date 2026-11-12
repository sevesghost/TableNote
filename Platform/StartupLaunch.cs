using System;
using System.Collections.Generic;
using System.Linq;

namespace TableNote;

public static class StartupLaunch
{
	public const string EntryName = "TableNote";

	public const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public static IStartupRegistration ForCurrentMachine()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsRunRegistration();
		}
		return XdgAutostartRegistration.ForCurrentUser();
	}

	public static string CurrentCommand()
	{
		string text = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = Environment.GetCommandLineArgs().FirstOrDefault() ?? "TableNote.exe";
		}
		// --startup: launched at login → start hidden in the tray so reminders keep running.
		return "\"" + text.Replace("\"", "") + "\" --startup";
	}

	public static StartupSyncResult SyncEntry(IDictionary<string, string> entries, bool enabled, string command)
	{
		if (!enabled)
		{
			entries.Remove("TableNote");
			return new StartupSyncResult(Ok: true, Enabled: false, null);
		}
		string text = (string.IsNullOrWhiteSpace(command) ? CurrentCommand() : command);
		if (!entries.TryGetValue("TableNote", out string value) || !string.Equals(value, text, StringComparison.Ordinal))
		{
			entries["TableNote"] = text;
		}
		return new StartupSyncResult(Ok: true, Enabled: true, null);
	}
}
