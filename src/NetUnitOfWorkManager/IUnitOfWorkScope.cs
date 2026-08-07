using System;

namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Represents a synchronous Unit of Work scope token.
    /// </summary>
    public interface IUnitOfWorkScope : IUnitOfWorkContext, IDisposable
    {
        /// <summary>
        /// Marks this scope as successfully completed.
        /// </summary>
        void Complete();

        /// <summary>
        /// Requests rollback for the Unit of Work and settles this scope.
        /// </summary>
        void Rollback();
    }
}
