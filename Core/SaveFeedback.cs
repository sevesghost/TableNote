namespace TableNote;
public static class SaveFeedback
{
    public static string Failure(Exception ex) => Localizer.T("save.failed", ex.Message);
}
