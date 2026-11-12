namespace TableNote;

public interface IStartupRegistration
{
	StartupSyncResult Apply(bool enabled, string command);
}
