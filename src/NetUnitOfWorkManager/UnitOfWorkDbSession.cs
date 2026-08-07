using System;
using System.Data.Common;

namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Exposes the provider-native connection and transaction borrowed from an active Unit of Work.
    /// </summary>
    public sealed class UnitOfWorkDbSession
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private readonly Action _ensureActive;

        internal UnitOfWorkDbSession(
            DbConnection connection,
            DbTransaction transaction,
            Action ensureActive)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _ensureActive = ensureActive ?? throw new ArgumentNullException(nameof(ensureActive));
        }

        /// <summary>
        /// Gets the provider-native connection borrowed from the active Unit of Work.
        /// The caller must not close or dispose this connection.
        /// </summary>
        public DbConnection Connection
        {
            get
            {
                _ensureActive();
                return _connection;
            }
        }

        /// <summary>
        /// Gets the provider-native transaction borrowed from the active Unit of Work.
        /// The caller must not commit, roll back, or dispose this transaction.
        /// </summary>
        public DbTransaction Transaction
        {
            get
            {
                _ensureActive();
                return _transaction;
            }
        }

        /// <summary>
        /// Creates a provider-native command and binds it to the current transaction.
        /// </summary>
        /// <returns>A provider-native command bound to the Unit of Work transaction.</returns>
        public DbCommand CreateCommand()
        {
            _ensureActive();

            DbCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            return command;
        }
    }
}
