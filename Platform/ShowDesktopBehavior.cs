namespace TableNote;

public static class ShowDesktopBehavior
{
	public static bool SurvivesShowDesktop(bool claimsDesktop, bool topmost)
	{
		if (claimsDesktop)
		{
			return !topmost;
		}
		return false;
	}
}
