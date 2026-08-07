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

        internal UnitOfWorkDbSession(DbConnection connection, DbTransaction transaction)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        /// <summary>
        /// Gets the provider-native connection owned by the active Unit of Work.
        /// </summary>
        public DbConnection Connection => _connection;

        /// <summary>
        /// Gets the provider-native transaction owned by the active Unit of Work.
        /// </summary>
        public DbTransaction Transaction => _transaction;

        /// <summary>
        /// Creates a provider-native command and binds it to the current transaction.
        /// </summary>
        /// <returns>A provider-native command bound to the Unit of Work transaction.</returns>
        public DbCommand CreateCommand()
        {
            DbCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;
            return command;
        }
    }
}
