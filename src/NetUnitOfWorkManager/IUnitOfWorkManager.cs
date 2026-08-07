namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Defines the ambient Unit of Work manager contract.
    /// </summary>
    public interface IUnitOfWorkManager
    {
        /// <summary>
        /// Gets a value indicating whether this manager has a current ambient Unit of Work.
        /// </summary>
        bool HasCurrent { get; }

        /// <summary>
        /// Gets the current ambient Unit of Work context.
        /// </summary>
        IUnitOfWorkContext Current { get; }

        /// <summary>
        /// Begins a Unit of Work scope.
        /// </summary>
        /// <param name="options">Optional transaction options.</param>
        /// <returns>A scope token representing the started Unit of Work scope.</returns>
        IUnitOfWorkScope Begin(UnitOfWorkOptions? options = null);
    }
}
