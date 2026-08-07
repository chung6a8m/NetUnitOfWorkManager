using System;
using System.Data;
using NetUnitOfWorkManager.Internal;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class RootUnitOfWorkTests
    {
        [Fact]
        public void Create_ClosedConnection_OpensExactlyOnce()
        {
            FakeDbConnection connection = new FakeDbConnection();

            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);

            Assert.Equal(1, connection.OpenCallCount);
            Assert.Equal(1, connection.BeginTransactionCallCount);

            root.RequestRollback();
            root.FinalizeTransaction();
        }

        [Fact]
        public void Create_AlreadyOpenConnection_DoesNotOpenAgain()
        {
            FakeDbConnection connection = new FakeDbConnection(ConnectionState.Open);

            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);

            Assert.Equal(0, connection.OpenCallCount);
            Assert.Equal(1, connection.BeginTransactionCallCount);

            root.RequestRollback();
            root.FinalizeTransaction();
        }

        [Fact]
        public void Create_WithIsolationLevel_StartsExactlyOneTransaction()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkOptions options = new UnitOfWorkOptions(IsolationLevel.Serializable);

            RootUnitOfWork root = RootUnitOfWork.Create(connection, options);

            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Equal(IsolationLevel.Serializable, connection.LastBeginIsolationLevel);

            root.RequestRollback();
            root.FinalizeTransaction();
        }

        [Fact]
        public void FinalizeTransaction_WithoutRollbackRequest_CommitsExactlyOnce()
        {
            FakeDbConnection connection = new FakeDbConnection();
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            root.FinalizeTransaction();

            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.Equal(UnitOfWorkLifecycleState.Disposed, root.State);

            Assert.Throws<UnitOfWorkStateException>(() => root.FinalizeTransaction());
            Assert.Equal(1, transaction.CommitCallCount);
        }

        [Fact]
        public void FinalizeTransaction_WithRollbackRequest_RollsBackExactlyOnce()
        {
            FakeDbConnection connection = new FakeDbConnection();
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            root.RequestRollback();
            root.FinalizeTransaction();

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.Equal(UnitOfWorkLifecycleState.Disposed, root.State);

            Assert.Throws<UnitOfWorkStateException>(() => root.FinalizeTransaction());
            Assert.Equal(1, transaction.RollbackCallCount);
        }

        [Fact]
        public void CommitFailure_MarksRootFaulted_AndIsNotRetried()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => root.FinalizeTransaction());

            Assert.Same(commitFailure, thrown);
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);

            Assert.Throws<UnitOfWorkStateException>(() => root.FinalizeTransaction());
            Assert.Equal(1, transaction.CommitCallCount);
        }

        [Fact]
        public void RollbackFailure_MarksRootFaulted_AndIsNotRetried()
        {
            InvalidOperationException rollbackFailure = new InvalidOperationException("rollback failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                RollbackException = rollbackFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            root.RequestRollback();
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => root.FinalizeTransaction());

            Assert.Same(rollbackFailure, thrown);
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);

            Assert.Throws<UnitOfWorkStateException>(() => root.FinalizeTransaction());
            Assert.Equal(1, transaction.RollbackCallCount);
        }

        [Fact]
        public void TransactionDisposeFailure_DoesNotPreventConnectionDisposeAttempt()
        {
            InvalidOperationException disposeFailure = new InvalidOperationException("transaction dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                TransactionDisposeException = disposeFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            root.RequestRollback();
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => root.FinalizeTransaction());

            Assert.Same(disposeFailure, thrown);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
        }

        [Fact]
        public void ConnectionDisposeFailure_IsSurfaced()
        {
            InvalidOperationException disposeFailure = new InvalidOperationException("connection dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                DisposeException = disposeFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);

            root.RequestRollback();
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => root.FinalizeTransaction());

            Assert.Same(disposeFailure, thrown);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
        }

        [Fact]
        public void Create_BeginTransactionFailure_DisposesConnection()
        {
            InvalidOperationException beginFailure = new InvalidOperationException("begin failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                BeginTransactionException = beginFailure
            };

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => RootUnitOfWork.Create(connection, null));

            Assert.Same(beginFailure, thrown);
            Assert.Equal(1, connection.OpenCallCount);
            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
        }

        [Fact]
        public void FinalizationAndCleanupFailures_AreAllPreserved()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            InvalidOperationException transactionDisposeFailure = new InvalidOperationException("transaction dispose failed");
            InvalidOperationException connectionDisposeFailure = new InvalidOperationException("connection dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                CommitException = commitFailure,
                TransactionDisposeException = transactionDisposeFailure,
                DisposeException = connectionDisposeFailure
            };
            RootUnitOfWork root = RootUnitOfWork.Create(connection, null);

            AggregateException thrown = Assert.Throws<AggregateException>(() => root.FinalizeTransaction());

            Assert.Collection(
                thrown.InnerExceptions,
                exception => Assert.Same(commitFailure, exception),
                exception => Assert.Same(transactionDisposeFailure, exception),
                exception => Assert.Same(connectionDisposeFailure, exception));
            Assert.Equal(UnitOfWorkLifecycleState.Faulted, root.State);
        }

        [Fact]
        public void Create_PrimaryAndCleanupFailures_AreBothPreserved()
        {
            InvalidOperationException beginFailure = new InvalidOperationException("begin failed");
            InvalidOperationException connectionDisposeFailure = new InvalidOperationException("connection dispose failed");
            FakeDbConnection connection = new FakeDbConnection
            {
                BeginTransactionException = beginFailure,
                DisposeException = connectionDisposeFailure
            };

            AggregateException thrown = Assert.Throws<AggregateException>(
                () => RootUnitOfWork.Create(connection, null));

            Assert.Collection(
                thrown.InnerExceptions,
                exception => Assert.Same(beginFailure, exception),
                exception => Assert.Same(connectionDisposeFailure, exception));
        }
    }
}
