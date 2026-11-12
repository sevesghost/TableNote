using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TableNote;

public sealed class WindowsRunRegistration : IStartupRegistration
{
	public StartupSyncResult Apply(bool enabled, string command)
	{
		if (!OperatingSystem.IsWindows())
		{
			return StartupSyncResult.Failed(Localizer.T("startup.windows_only"));
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				return StartupSyncResult.Failed(Localizer.T("startup.open_key_failed"));
			}
			if (!enabled)
			{
				if (registryKey.GetValue("TableNote") != null)
				{
					registryKey.DeleteValue("TableNote", throwOnMissingValue: false);
				}
				return new StartupSyncResult(Ok: true, Enabled: false, null);
			}
			string text = (string.IsNullOrWhiteSpace(command) ? StartupLaunch.CurrentCommand() : command);
			if (!string.Equals(registryKey.GetValue("TableNote") as string, text, StringComparison.Ordinal))
			{
				registryKey.SetValue("TableNote", text);
			}
			return new StartupSyncResult(Ok: true, Enabled: true, null);
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException) ? true : false)
		{
			return StartupSyncResult.Failed(Localizer.T("ui.startup_failed", ex.Message));
		}
	}
}
