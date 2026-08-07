namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Exposes the database session and rollback-only state for an active Unit of Work.
    /// </summary>
    public interface IUnitOfWorkContext
    {
        /// <summary>
        /// Gets the provider-native database session associated with the Unit of Work.
        /// </summary>
        UnitOfWorkDbSession Db { get; }

        /// <summary>
        /// Gets a value indicating whether rollback has been requested for the Unit of Work.
        /// </summary>
        bool IsRollbackRequested { get; }
    }
}
