namespace NetUnitOfWorkManager.Internal
{
    internal enum UnitOfWorkScopeState
    {
        Active = 0,
        Completed = 1,
        RolledBack = 2,
        Abandoned = 3
    }
}
