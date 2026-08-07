namespace NetUnitOfWorkManager.Internal
{
    internal enum UnitOfWorkLifecycleState
    {
        Active = 0,
        Finalizing = 1,
        Disposed = 2,
        Faulted = 3
    }
}
