using System;
using System.Data;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class UnitOfWorkManagerTests
    {
        [Fact]
        public void Nested_Begin_Returns_Different_Scope_Objects()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);

            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();

            Assert.NotSame(outer, inner);

            inner.Complete();
            outer.Complete();
        }

        [Fact]
        public void Nested_Begin_Reuses_One_Physical_Transaction()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);

            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();

            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Same(outer.Db.Transaction, inner.Db.Transaction);

            inner.Complete();
            outer.Complete();
        }

        [Fact]
        public void Inner_Complete_Does_Not_Commit_Physical_Transaction()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            inner.Complete();

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);

            outer.Rollback();
        }

        [Fact]
        public void Outer_Complete_Commits_After_Inner_Complete()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            inner.Complete();
            outer.Complete();

            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
        }

        [Fact]
        public void Inner_Rollback_Forces_Final_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            inner.Rollback();

            Assert.True(outer.IsRollbackRequested);
            Assert.Equal(0, transaction.RollbackCallCount);

            outer.Complete();

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
        }

        [Fact]
        public void Inner_Abandon_Forces_Final_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            inner.Dispose();

            Assert.True(outer.IsRollbackRequested);

            outer.Complete();

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
        }

        [Fact]
        public void Inner_Dispose_Does_Not_Dispose_Root_Resources()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkScope inner = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            inner.Dispose();

            Assert.Equal(0, transaction.DisposeCallCount);
            Assert.Equal(0, connection.DisposeCallCount);

            outer.Complete();
        }

        [Fact]
        public void Double_Complete_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Complete();

            Assert.Throws<UnitOfWorkStateException>(() => scope.Complete());
        }

        [Fact]
        public void Rollback_After_Complete_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Complete();

            Assert.Throws<UnitOfWorkStateException>(() => scope.Rollback());
        }

        [Fact]
        public void Complete_After_Rollback_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Rollback();

            Assert.Throws<UnitOfWorkStateException>(() => scope.Complete());
        }

        [Fact]
        public void Nested_Different_IsolationLevel_Throws()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable));

            Assert.Throws<UnitOfWorkStateException>(
                () => manager.Begin(new UnitOfWorkOptions(IsolationLevel.ReadCommitted)));

            outer.Rollback();
        }

        [Fact]
        public void Nested_Begin_Without_Options_Inherits_Root_Isolation()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable));

            IUnitOfWorkScope inner = manager.Begin();

            Assert.Same(outer.Db.Transaction, inner.Db.Transaction);

            inner.Complete();
            outer.Complete();
        }

        [Fact]
        public void Two_Manager_Instances_Do_Not_Share_Ambient_Root()
        {
            FakeDbConnection firstConnection = new FakeDbConnection();
            FakeDbConnection secondConnection = new FakeDbConnection();
            UnitOfWorkManager firstManager = CreateManager(firstConnection);
            UnitOfWorkManager secondManager = CreateManager(secondConnection);

            IUnitOfWorkScope firstScope = firstManager.Begin();
            IUnitOfWorkScope secondScope = secondManager.Begin();

            Assert.True(firstManager.HasCurrent);
            Assert.True(secondManager.HasCurrent);
            Assert.NotSame(firstScope.Db.Transaction, secondScope.Db.Transaction);

            secondScope.Complete();
            firstScope.Complete();
        }

        [Fact]
        public void Ambient_Is_Cleared_After_Commit()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            Assert.True(manager.HasCurrent);
            Assert.Same(scope.Db, manager.Current.Db);

            scope.Complete();

            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
        }

        [Fact]
        public void Ambient_Is_Cleared_After_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Rollback();

            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
        }

        [Fact]
        public void Ambient_Is_Cleared_After_Finalization_Failure()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => scope.Complete());

            Assert.Same(commitFailure, thrown);
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
        }

        [Fact]
        public void Begin_Failure_Does_Not_Publish_Ambient_Root()
        {
            InvalidOperationException beginFailure = new InvalidOperationException("begin failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                BeginTransactionException = beginFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(beginFailure, thrown);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Settled_Scope_Cannot_Access_Db()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();

            scope.Complete();

            Assert.Throws<UnitOfWorkStateException>(() => scope.Db);
        }

        private static UnitOfWorkManager CreateManager(FakeDbConnection connection)
        {
            return new UnitOfWorkManager(() => connection);
        }
    }
}
