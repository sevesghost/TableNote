namespace TableNote;

public readonly record struct DeletePlan(bool CanDelete, bool NeedsConfirm, string StatusIfBlocked, string ConfirmMessage);
