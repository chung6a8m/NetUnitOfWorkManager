using System.Data;

namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Defines provider-neutral options for starting a Unit of Work.
    /// </summary>
    public sealed class UnitOfWorkOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnitOfWorkOptions"/> class.
        /// </summary>
        /// <param name="isolationLevel">The requested transaction isolation level, or <see langword="null"/> to use the provider default.</param>
        public UnitOfWorkOptions(IsolationLevel? isolationLevel = null)
        {
            IsolationLevel = isolationLevel;
        }

        /// <summary>
        /// Gets the requested transaction isolation level, or <see langword="null"/> when the provider default should be used.
        /// </summary>
        public IsolationLevel? IsolationLevel { get; }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is UnitOfWorkOptions other && IsolationLevel == other.IsolationLevel;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return IsolationLevel.HasValue ? (int)IsolationLevel.Value : 0;
        }
    }
}
