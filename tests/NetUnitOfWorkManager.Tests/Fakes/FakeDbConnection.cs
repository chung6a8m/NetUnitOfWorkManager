using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace NetUnitOfWorkManager.Tests.Fakes
{
    internal sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState _state;
        private string _connectionString = string.Empty;

        internal FakeDbConnection(ConnectionState initialState = ConnectionState.Closed)
        {
            _state = initialState;
        }

        internal int OpenCallCount { get; private set; }

        internal int BeginTransactionCallCount { get; private set; }

        internal int DisposeCallCount { get; private set; }

        internal Exception? OpenException { get; set; }

        internal Exception? BeginTransactionException { get; set; }

        internal Exception? DisposeException { get; set; }

        internal Exception? CommitException { get; set; }

        internal Exception? RollbackException { get; set; }

        internal Exception? TransactionDisposeException { get; set; }

        internal FakeDbTransaction? LastTransaction { get; private set; }

        internal IsolationLevel? LastBeginIsolationLevel { get; private set; }

#if NET8_0_OR_GREATER
        [AllowNull]
#endif
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "FakeDatabase";

        public override string DataSource => "FakeDataSource";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCallCount++;

            if (OpenException != null)
            {
                throw OpenException;
            }

            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginTransactionCallCount++;
            LastBeginIsolationLevel = isolationLevel;

            if (BeginTransactionException != null)
            {
                throw BeginTransactionException;
            }

            FakeDbTransaction transaction = new FakeDbTransaction(this, isolationLevel)
            {
                CommitException = CommitException,
                RollbackException = RollbackException,
                DisposeException = TransactionDisposeException
            };

            LastTransaction = transaction;
            return transaction;
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;
                _state = ConnectionState.Closed;

                if (DisposeException != null)
                {
                    throw DisposeException;
                }
            }

            base.Dispose(disposing);
        }
    }
}
