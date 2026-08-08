using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class AsyncFlowHardeningTests
    {
        [Fact]
        public async Task Root_Ambient_Survives_Await_Continuation()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope scope = manager.Begin();
            IUnitOfWorkContext context = manager.Current;

            await Task.Yield();

            Assert.True(manager.HasCurrent);
            Assert.Same(context, manager.Current);
            Assert.Same(scope.Db.Transaction, manager.Current.Db.Transaction);

            scope.Complete();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public async Task Nested_Scope_Started_After_Await_Reuses_Same_Root()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();

            await Task.Yield();

            IUnitOfWorkScope inner = manager.Begin();

            Assert.Same(outer.Db.Connection, inner.Db.Connection);
            Assert.Same(outer.Db.Transaction, inner.Db.Transaction);
            Assert.Equal(1, connection.BeginTransactionCallCount);

            inner.Complete();
            outer.Complete();
        }

        [Fact]
        public async Task Suppression_Remains_Effective_Across_Await()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
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
        public async Task Outer_Root_Restores_After_Awaited_Suppression_Region()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;

            using (manager.Suppress())
            {
                await Task.Yield();
                Assert.False(manager.HasCurrent);
            }

            Assert.True(manager.HasCurrent);
            Assert.Same(outerContext, manager.Current);

            outer.Complete();
        }

        [Fact]
        public async Task Child_Task_Created_While_Suppressed_Observes_Suppressed_Ambient()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();

            using (manager.Suppress())
            {
                await Task.Run(() =>
                {
                    Assert.False(manager.HasCurrent);
                    Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
                }, TestContext.Current.CancellationToken);

                Assert.False(manager.HasCurrent);
            }

            Assert.True(manager.HasCurrent);
            outer.Rollback();
        }

        [Fact]
        public async Task Manager_Remains_Usable_After_Awaited_Suppression_Exception()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection freshConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, freshConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ThrowAfterAwaitInsideSuppression(manager));

            Assert.Equal("suppressed async failure", thrown.Message);
            Assert.Same(outerContext, manager.Current);

            outer.Rollback();
            Assert.False(manager.HasCurrent);

            IUnitOfWorkScope fresh = manager.Begin();
            Assert.NotSame(outerContext, manager.Current);
            fresh.Complete();

            Assert.False(manager.HasCurrent);
        }

        private static async Task ThrowAfterAwaitInsideSuppression(UnitOfWorkManager manager)
        {
            using (manager.Suppress())
            {
                await Task.Yield();
                throw new InvalidOperationException("suppressed async failure");
            }
        }

        private static UnitOfWorkManager CreateQueuedManager(params FakeDbConnection[] connections)
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
