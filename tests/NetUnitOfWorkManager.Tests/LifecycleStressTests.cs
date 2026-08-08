using System;
using System.Collections.Generic;
using System.Data;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class LifecycleStressTests
    {
        private const int NestedDepth = 64;
        private const int SequentialRootCount = 200;

        [Fact]
        public void SixtyFour_Level_Nested_Complete_Finalizes_Physical_Transaction_Exactly_Once()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            List<IUnitOfWorkScope> scopes = BeginNestedScopes(manager, NestedDepth);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);

            CompleteScopesInReverse(scopes);

            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Equal(1, transaction.CommitCallCount);
            Assert.Equal(0, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void SixtyFour_Level_Nested_Rollback_At_Inner_Level_Forces_One_Final_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            List<IUnitOfWorkScope> scopes = BeginNestedScopes(manager, NestedDepth);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);
            int rollbackIndex = NestedDepth / 2;

            scopes[rollbackIndex].Rollback();

            for (int index = scopes.Count - 1; index >= 0; index--)
            {
                if (index != rollbackIndex)
                {
                    scopes[index].Complete();
                }
            }

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void SixtyFour_Level_Nested_Abandon_Forces_One_Final_Rollback()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            List<IUnitOfWorkScope> scopes = BeginNestedScopes(manager, NestedDepth);
            FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);
            int abandonedIndex = scopes.Count - 1;

            scopes[abandonedIndex].Dispose();

            for (int index = abandonedIndex - 1; index >= 0; index--)
            {
                scopes[index].Complete();
            }

            Assert.Equal(0, transaction.CommitCallCount);
            Assert.Equal(1, transaction.RollbackCallCount);
            Assert.Equal(1, transaction.DisposeCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Two_Hundred_Sequential_Roots_Leave_No_Stale_Ambient_State()
        {
            List<FakeDbConnection> connections = new List<FakeDbConnection>();
            UnitOfWorkManager manager = new UnitOfWorkManager(() =>
            {
                FakeDbConnection connection = new FakeDbConnection();
                connections.Add(connection);
                return connection;
            });

            for (int index = 0; index < SequentialRootCount; index++)
            {
                using (IUnitOfWorkScope scope = manager.Begin())
                {
                    Assert.True(manager.HasCurrent);
                    Assert.Same(scope.Db, manager.Current.Db);
                    scope.Complete();
                }

                Assert.False(manager.HasCurrent);
                Assert.Throws<UnitOfWorkStateException>(() => manager.Current);
            }

            Assert.Equal(SequentialRootCount, connections.Count);

            foreach (FakeDbConnection connection in connections)
            {
                FakeDbTransaction transaction = Assert.IsType<FakeDbTransaction>(connection.LastTransaction);
                Assert.Equal(1, connection.BeginTransactionCallCount);
                Assert.Equal(1, transaction.CommitCallCount);
                Assert.Equal(1, transaction.DisposeCallCount);
                Assert.Equal(1, connection.DisposeCallCount);
            }
        }

        [Fact]
        public void Connection_Factory_Can_Return_Already_Open_Connection()
        {
            FakeDbConnection connection = new FakeDbConnection(ConnectionState.Open);
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);

            IUnitOfWorkScope scope = manager.Begin();
            scope.Complete();

            Assert.Equal(0, connection.OpenCallCount);
            Assert.Equal(1, connection.BeginTransactionCallCount);
            Assert.Equal(1, connection.DisposeCallCount);
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Begin_Failure_After_Previous_Successful_Root_Does_Not_Restore_Stale_Root()
        {
            FakeDbConnection first = new FakeDbConnection();
            InvalidOperationException beginFailure = new InvalidOperationException("begin failed");
            FakeDbConnection failing = new FakeDbConnection
            {
                BeginTransactionException = beginFailure
            };
            FakeDbConnection third = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(first, failing, third);

            IUnitOfWorkScope firstScope = manager.Begin();
            IUnitOfWorkContext firstContext = manager.Current;
            firstScope.Complete();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(beginFailure, thrown);
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            IUnitOfWorkScope thirdScope = manager.Begin();
            Assert.NotSame(firstContext, manager.Current);
            thirdScope.Complete();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Commit_Failure_Cannot_Make_Manager_Reuse_Faulted_Root()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("commit failed");
            FakeDbConnection failing = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            FakeDbConnection fresh = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(failing, fresh);
            IUnitOfWorkScope failedScope = manager.Begin();
            IUnitOfWorkContext failedContext = manager.Current;

            Assert.Throws<InvalidOperationException>(() => failedScope.Complete());
            Assert.False(manager.HasCurrent);

            IUnitOfWorkScope freshScope = manager.Begin();
            Assert.NotSame(failedContext, manager.Current);
            freshScope.Complete();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Rollback_Failure_Cannot_Make_Manager_Reuse_Faulted_Root()
        {
            InvalidOperationException rollbackFailure = new InvalidOperationException("rollback failed");
            FakeDbConnection failing = new FakeDbConnection
            {
                RollbackException = rollbackFailure
            };
            FakeDbConnection fresh = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(failing, fresh);
            IUnitOfWorkScope failedScope = manager.Begin();
            IUnitOfWorkContext failedContext = manager.Current;

            Assert.Throws<InvalidOperationException>(() => failedScope.Rollback());
            Assert.False(manager.HasCurrent);

            IUnitOfWorkScope freshScope = manager.Begin();
            Assert.NotSame(failedContext, manager.Current);
            freshScope.Complete();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Cleanup_Failure_Still_Allows_Fresh_Subsequent_Root_Begin()
        {
            InvalidOperationException cleanupFailure = new InvalidOperationException("transaction dispose failed");
            FakeDbConnection failing = new FakeDbConnection
            {
                TransactionDisposeException = cleanupFailure
            };
            FakeDbConnection fresh = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(failing, fresh);
            IUnitOfWorkScope failedScope = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => failedScope.Complete());

            Assert.Same(cleanupFailure, thrown);
            Assert.False(manager.HasCurrent);

            IUnitOfWorkScope freshScope = manager.Begin();
            freshScope.Complete();

            Assert.False(manager.HasCurrent);
            Assert.Equal(1, Assert.IsType<FakeDbTransaction>(fresh.LastTransaction).CommitCallCount);
        }

        [Fact]
        public void Multiple_Manager_Instances_Remain_Isolated_Through_Repeated_Use()
        {
            List<FakeDbConnection> firstConnections = new List<FakeDbConnection>();
            List<FakeDbConnection> secondConnections = new List<FakeDbConnection>();
            UnitOfWorkManager firstManager = CreateTrackingManager(firstConnections);
            UnitOfWorkManager secondManager = CreateTrackingManager(secondConnections);

            for (int index = 0; index < 50; index++)
            {
                IUnitOfWorkScope firstScope = firstManager.Begin();
                IUnitOfWorkScope secondScope = secondManager.Begin();

                Assert.True(firstManager.HasCurrent);
                Assert.True(secondManager.HasCurrent);
                Assert.NotSame(firstScope.Db.Transaction, secondScope.Db.Transaction);

                secondScope.Complete();
                Assert.False(secondManager.HasCurrent);
                Assert.True(firstManager.HasCurrent);

                firstScope.Complete();
                Assert.False(firstManager.HasCurrent);
            }

            Assert.Equal(50, firstConnections.Count);
            Assert.Equal(50, secondConnections.Count);
        }

        private static List<IUnitOfWorkScope> BeginNestedScopes(UnitOfWorkManager manager, int depth)
        {
            List<IUnitOfWorkScope> scopes = new List<IUnitOfWorkScope>(depth);

            for (int index = 0; index < depth; index++)
            {
                scopes.Add(manager.Begin());
            }

            return scopes;
        }

        private static void CompleteScopesInReverse(List<IUnitOfWorkScope> scopes)
        {
            for (int index = scopes.Count - 1; index >= 0; index--)
            {
                scopes[index].Complete();
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

        private static UnitOfWorkManager CreateTrackingManager(List<FakeDbConnection> connections)
        {
            return new UnitOfWorkManager(() =>
            {
                FakeDbConnection connection = new FakeDbConnection();
                connections.Add(connection);
                return connection;
            });
        }
    }
}
