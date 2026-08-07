using System;
using System.Data;
using System.Data.Common;

namespace NetUnitOfWorkManager.Tests.Fakes
{
    internal sealed class FakeDbTransaction : DbTransaction
    {
        private readonly DbConnection _connection;

        internal FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            IsolationLevel = isolationLevel;
        }

        internal int CommitCallCount { get; private set; }

        internal int RollbackCallCount { get; private set; }

        internal int DisposeCallCount { get; private set; }

        internal Exception? CommitException { get; set; }

        internal Exception? RollbackException { get; set; }

        internal Exception? DisposeException { get; set; }

        public override IsolationLevel IsolationLevel { get; }

        protected override DbConnection DbConnection => _connection;

        public override void Commit()
        {
            CommitCallCount++;

            if (CommitException != null)
            {
                throw CommitException;
            }
        }

        public override void Rollback()
        {
            RollbackCallCount++;

            if (RollbackException != null)
            {
                throw RollbackException;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;

                if (DisposeException != null)
                {
                    throw DisposeException;
                }
            }

            base.Dispose(disposing);
        }
    }
}
