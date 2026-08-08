using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class AmbientSuppressionTests
    {
        [Fact]
        public void Suppress_Hides_Current_Ambient_Root()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();

            using (manager.Suppress())
            {
                Assert.False(manager.HasCurrent);
            }

            outer.Rollback();
        }

        [Fact]
        public void Suppress_Current_Throws_While_No_Inner_Root()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();

            using (manager.Suppress())
            {
                Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
            }

            outer.Rollback();
        }

        [Fact]
        public void Suppress_Dispose_Restores_Exact_Outer_Root()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;

            IDisposable suppression = manager.Suppress();
            suppression.Dispose();

            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppress_Without_Current_Is_Valid()
        {
            UnitOfWorkManager manager = CreateManager(new FakeDbConnection());

            IDisposable suppression = manager.Suppress();

            Assert.False(manager.HasCurrent);
            suppression.Dispose();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Nested_Suppress_Restores_In_Lifo_Order()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable first = manager.Suppress();
            IDisposable second = manager.Suppress();

            Assert.False(manager.HasCurrent);

            second.Dispose();
            Assert.False(manager.HasCurrent);

            first.Dispose();
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppression_Double_Dispose_Is_Idempotent()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();

            suppression.Dispose();
            suppression.Dispose();

            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppression_Out_Of_Order_Dispose_Throws_Without_Corrupting_Ambient()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable first = manager.Suppress();
            IDisposable second = manager.Suppress();

            Assert.Throws<UnitOfWorkStateException>(() => first.Dispose());
            Assert.False(manager.HasCurrent);

            second.Dispose();
            Assert.False(manager.HasCurrent);

            first.Dispose();
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppression_Cannot_Be_Disposed_While_Independent_Root_Is_Active()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();
            IUnitOfWorkContext innerContext = manager.Current;

            Assert.Throws<UnitOfWorkStateException>(() => suppression.Dispose());
            Assert.Same(innerContext, manager.Current);

            inner.Rollback();
            Assert.False(manager.HasCurrent);

            suppression.Dispose();
            Assert.Same(outer.Db, manager.Current.Db);
            outer.Rollback();
        }

        [Fact]
        public void Begin_Inside_Suppression_Creates_Different_Physical_Transaction()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            Assert.NotSame(outer.Db.Transaction, inner.Db.Transaction);
            Assert.Equal(1, outerConnection.BeginTransactionCallCount);
            Assert.Equal(1, innerConnection.BeginTransactionCallCount);

            inner.Rollback();
            suppression.Dispose();
            outer.Rollback();
        }

        [Fact]
        public void Begin_Inside_Suppression_Allows_Different_IsolationLevel()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable));
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin(new UnitOfWorkOptions(IsolationLevel.ReadCommitted));

            Assert.Equal(IsolationLevel.Serializable, outer.Db.Transaction.IsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, inner.Db.Transaction.IsolationLevel);

            inner.Rollback();
            suppression.Dispose();
            outer.Rollback();
        }

        [Fact]
        public void Independent_Root_Commit_Returns_To_Suppressed_State()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            inner.Complete();

            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Independent_Root_Rollback_Returns_To_Suppressed_State()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            inner.Rollback();

            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Independent_Root_Finalization_Failure_Returns_To_Suppressed_State()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            InvalidOperationException commitFailure = new InvalidOperationException("inner commit failed");
            FakeDbConnection innerConnection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            UnitOfWorkManager manager = CreateManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => inner.Complete());

            Assert.Same(commitFailure, thrown);
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Exception_Inside_Suppression_Restores_Outer_Ambient()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            InvalidOperationException expected = new InvalidOperationException("boom");

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            {
                using (manager.Suppress())
                {
                    throw expected;
                }
            });

            Assert.Same(expected, thrown);
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppress_One_Manager_Does_Not_Affect_Another_Manager()
        {
            FakeDbConnection firstConnection = new FakeDbConnection();
            FakeDbConnection secondConnection = new FakeDbConnection();
            UnitOfWorkManager firstManager = CreateManager(firstConnection);
            UnitOfWorkManager secondManager = CreateManager(secondConnection);
            IUnitOfWorkScope firstScope = firstManager.Begin();
            IUnitOfWorkScope secondScope = secondManager.Begin();
            IUnitOfWorkContext secondContext = secondManager.Current;

            using (firstManager.Suppress())
            {
                Assert.False(firstManager.HasCurrent);
                Assert.True(secondManager.HasCurrent);
                Assert.Same(secondContext, secondManager.Current);
            }

            secondScope.Rollback();
            firstScope.Rollback();
        }

        [Fact]
        public async Task Suppression_Flows_Across_Await()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();

            using (manager.Suppress())
            {
                await Task.Yield();
                Assert.False(manager.HasCurrent);
                Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
            }

            outer.Rollback();
        }

        [Fact]
        public async Task Outer_Ambient_Flows_Across_Await_After_Suppression_Restore()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;

            using (manager.Suppress())
            {
                Assert.False(manager.HasCurrent);
            }

            await Task.Yield();

            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Suppress_Does_Not_Touch_Database_Lifecycle()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateManager(connection);
            IUnitOfWorkScope outer = manager.Begin();
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);
            int openCalls = connection.OpenCallCount;
            int beginCalls = connection.BeginTransactionCallCount;

            IDisposable suppression = manager.Suppress();
            suppression.Dispose();

            Assert.Equal(openCalls, connection.OpenCallCount);
            Assert.Equal(beginCalls, connection.BeginTransactionCallCount);
            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
            Assert.Equal(0, transaction.DisposeCallCount);
            Assert.Equal(0, connection.DisposeCallCount);

            outer.Rollback();
        }

        private static UnitOfWorkManager CreateManager(params FakeDbConnection[] connections)
        {
            Queue<FakeDbConnection> queue = new Queue<FakeDbConnection>(connections);

            return new UnitOfWorkManager(() =>
            {
                if (queue.Count == 0)
                {
                    throw new InvalidOperationException("No fake connection remains for a new root Unit of Work.");
                }

                return queue.Dequeue();
            });
        }
    }
}
