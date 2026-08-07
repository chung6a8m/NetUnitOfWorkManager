using System;
using NetUnitOfWorkManager.Internal;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class FailureCleanupTests
    {
        [Fact]
        public void ConnectionFactoryFailure_DoesNotPublishAmbientRoot()
        {
            InvalidOperationException factoryFailure = new InvalidOperationException("factory failed");
            UnitOfWorkManager manager = new UnitOfWorkManager(
                () => throw factoryFailure);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(factoryFailure, thrown);
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
        }

        [Fact]
        public void ConnectionOpenFailure_DisposesConnection_AndDoesNotPublishAmbientRoot()
        {
            InvalidOperationException openFailure = new InvalidOperationException("open failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                OpenException = openFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(openFailure, thrown);
            Assert.Equal(1, connection.OpenCallCount);
            Assert.Equal(0, connection.BeginTransactionCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void BeginTransactionFailure_DisposesConnection_AndDoesNotPublishAmbientRoot()
        {
            InvalidOperationException beginFailure = new InvalidOperationException("begin failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                BeginTransactionException = beginFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(beginFailure, thrown);
            Assert.Equal(1, connection.OpenCallCount);
            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void CommitFailure_DoesNotRollback_AndStillAttemptsAllCleanup()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => scope.Complete());

            Assert.Same(commitFailure, thrown);
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void RollbackFailure_StillAttemptsAllCleanup()
        {
            InvalidOperationException rollbackFailure = new InvalidOperationException("rollback failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                RollbackException = rollbackFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => scope.Rollback());

            Assert.Same(rollbackFailure, thrown);
            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void TransactionDisposeFailure_DoesNotPreventConnectionDispose_AndClearsAmbient()
        {
            InvalidOperationException transactionDisposeFailure = new InvalidOperationException("transaction dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                TransactionDisposeException = transactionDisposeFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => scope.Complete());

            Assert.Same(transactionDisposeFailure, thrown);
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void ConnectionDisposeFailure_IsSurfaced_AfterTransactionDispose_AndClearsAmbient()
        {
            InvalidOperationException connectionDisposeFailure = new InvalidOperationException("connection dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                DisposeException = connectionDisposeFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => scope.Complete());

            Assert.Same(connectionDisposeFailure, thrown);
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void CommitAndTransactionDisposeFailures_AreBothPreserved_WithoutRollbackRetry()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            InvalidOperationException transactionDisposeFailure = new InvalidOperationException("transaction dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure,
                TransactionDisposeException = transactionDisposeFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            AggregateException thrown = Assert.Throws<AggregateException>(() => scope.Complete());

            Assert.Collection(
                thrown.InnerExceptions,
                exception => Assert.Same(commitFailure, exception),
                exception => Assert.Same(transactionDisposeFailure, exception));
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void RollbackAndConnectionDisposeFailures_AreBothPreserved()
        {
            InvalidOperationException rollbackFailure = new InvalidOperationException("rollback failed");
            InvalidOperationException connectionDisposeFailure = new InvalidOperationException("connection dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                RollbackException = rollbackFailure,
                DisposeException = connectionDisposeFailure
            };
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            AggregateException thrown = Assert.Throws<AggregateException>(() => scope.Rollback());

            Assert.Collection(
                thrown.InnerExceptions,
                exception => Assert.Same(rollbackFailure, exception),
                exception => Assert.Same(connectionDisposeFailure, exception));
            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void FinalizationFailure_LeavesRootFaulted_AndCannotBecomeActiveAgain()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);

            Assert.Throws<InvalidOperationException>(() => root.FinalizeTransaction());

            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
            Assert.Throws<UnitOfWorkStateException>(() => root.AcquireScope());
            Assert.Throws<UnitOfWorkStateException>(() => root.Db);
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
        }

        [Fact]
        public void NestedBegin_DuringFinalizing_IsRejectedDeterministically()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope scope = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);
            bool nestedBeginAttempted = false;

            transaction.CommitCallback = () =>
            {
                nestedBeginAttempted = true;
                UnitOfWorkStateException nestedFailure = Assert.Throws<UnitOfWorkStateException>(() => manager.Begin());
                Assert.Contains("Finalizing", nestedFailure.Message);
            };

            scope.Complete();

            Assert.True(nestedBeginAttempted);
            Assert.False(manager.HasCurrent);
        }

        private static UnitOfWorkManager CreateManager(FakeDbConnection connection)
        {
            return new UnitOfWorkManager(() => connection);
        }
    }
}
