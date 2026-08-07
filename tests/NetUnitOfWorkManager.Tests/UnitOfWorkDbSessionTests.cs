using System.Data.Common;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class UnitOfWorkDbSessionTests
    {
        [Fact]
        public void CreateCommand_Returns_Provider_Native_Command()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            DbCommand command = scope.Db.CreateCommand();

            Assert.IsType<FakeDbCommand>(command);
            Assert.Equal(1, connection.CreateCommandCallCount);

            command.Dispose();
            scope.Rollback();
        }

        [Fact]
        public void CreateCommand_Binds_Current_Transaction()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            DbTransaction transaction = scope.Db.Transaction;
            DbCommand command = scope.Db.CreateCommand();

            Assert.Same(transaction, command.Transaction);

            command.Dispose();
            scope.Rollback();
        }

        [Fact]
        public void CreateCommand_Uses_The_Root_Connection()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            DbCommand command = scope.Db.CreateCommand();

            Assert.Same(connection, scope.Db.Connection);
            Assert.Same(connection, command.Connection);

            command.Dispose();
            scope.Rollback();
        }

        [Fact]
        public void Db_After_Scope_Settled_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Complete();

            Assert.Throws<UnitOfWorkStateException>(() => scope.Db);
        }

        [Fact]
        public void Db_After_Root_Finalized_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            UnitOfWorkDbSession db = scope.Db;

            scope.Complete();

            Assert.Throws<UnitOfWorkStateException>(() => db.Connection);
            Assert.Throws<UnitOfWorkStateException>(() => db.Transaction);
            Assert.Throws<UnitOfWorkStateException>(() => db.CreateCommand());
        }

        [Fact]
        public void IsRollbackRequested_Becomes_True_After_Inner_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();

            inner.Rollback();

            Assert.True(outer.IsRollbackRequested);

            outer.Complete();
        }

        private static UnitOfWorkManager CreateManager(FakeDbConnection connection)
        {
            return new UnitOfWorkManager(() => connection);
        }
    }
}
