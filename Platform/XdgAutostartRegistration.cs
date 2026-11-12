using System;
using System.IO;

namespace TableNote;

public sealed class XdgAutostartRegistration : IStartupRegistration
{
	private readonly string _directory;

	public string FilePath => Path.Combine(_directory, "TableNote.desktop");

	public XdgAutostartRegistration(string directory)
	{
		_directory = directory;
	}

	public static XdgAutostartRegistration ForCurrentUser()
	{
		string text = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (string.IsNullOrWhiteSpace(text))
		{
			string text2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(text2))
			{
				text2 = Path.GetTempPath();
			}
			text = Path.Combine(text2, ".config");
		}
		return new XdgAutostartRegistration(Path.Combine(text, "autostart"));
	}

	public StartupSyncResult Apply(bool enabled, string command)
	{
		try
		{
			if (!enabled)
			{
				if (File.Exists(FilePath))
				{
					File.Delete(FilePath);
				}
				return new StartupSyncResult(Ok: true, Enabled: false, null);
			}
			string text = Render(string.IsNullOrWhiteSpace(command) ? StartupLaunch.CurrentCommand() : command);
			if (File.Exists(FilePath) && string.Equals(File.ReadAllText(FilePath), text, StringComparison.Ordinal))
			{
				return new StartupSyncResult(Ok: true, Enabled: true, null);
			}
			Directory.CreateDirectory(_directory);
			string text2 = FilePath + ".tmp";
			File.WriteAllText(text2, text);
			File.Move(text2, FilePath, overwrite: true);
			return new StartupSyncResult(Ok: true, Enabled: true, null);
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
		{
			return StartupSyncResult.Failed(Localizer.T("ui.startup_failed", ex.Message));
		}
	}

	public static string Render(string command)
	{
		return "[Desktop Entry]\nType=Application\nName=TableNote\nExec=" + command + "\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
	}
}
