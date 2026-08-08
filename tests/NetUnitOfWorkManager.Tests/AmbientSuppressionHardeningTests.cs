using System;
using System.Collections.Generic;
using NetUnitOfWorkManager.Tests.Fakes;
using Xunit;

namespace NetUnitOfWorkManager.Tests
{
    public sealed class AmbientSuppressionHardeningTests
    {
        private const int SuppressionDepth = 32;
        private const int SuppressionRepeatCount = 200;

        [Fact]
        public void ThirtyTwo_Level_Nested_Suppression_Restores_Exact_Outer_Root()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            List<IDisposable> tokens = new List<IDisposable>(SuppressionDepth);

            for (int index = 0; index < SuppressionDepth; index++)
            {
                tokens.Add(manager.Suppress());
                Assert.False(manager.HasCurrent);
            }

            for (int index = tokens.Count - 1; index >= 0; index--)
            {
                tokens[index].Dispose();

                if (index > 0)
                {
                    Assert.False(manager.HasCurrent);
                }
            }

            Assert.True(manager.HasCurrent);
            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Two_Hundred_Repeated_Suppress_Restore_Cycles_Leave_No_Stale_Frame()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;

            for (int index = 0; index < SuppressionRepeatCount; index++)
            {
                IDisposable token = manager.Suppress();
                Assert.False(manager.HasCurrent);
                token.Dispose();

                Assert.True(manager.HasCurrent);
                Assert.Same(outerContext, manager.Current);
            }

            outer.Complete();
            Assert.False(manager.HasCurrent);
        }

        [Fact]
        public void Out_Of_Order_Dispose_Can_Be_Retried_After_Inner_Boundaries_End()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable first = manager.Suppress();
            IDisposable second = manager.Suppress();
            IDisposable third = manager.Suppress();

            Assert.Throws<UnitOfWorkStateException>(() => first.Dispose());
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            third.Dispose();
            Assert.False(manager.HasCurrent);
            second.Dispose();
            Assert.False(manager.HasCurrent);
            first.Dispose();

            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Failed_Out_Of_Order_Dispose_Does_Not_Change_Ambient_Or_Outer_Identity()
        {
            FakeDbConnection connection = new FakeDbConnection();
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable first = manager.Suppress();
            IDisposable second = manager.Suppress();

            UnitOfWorkStateException thrown = Assert.Throws<UnitOfWorkStateException>(() => first.Dispose());

            Assert.Contains("LIFO", thrown.Message);
            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            second.Dispose();
            Assert.False(manager.HasCurrent);
            first.Dispose();

            Assert.Same(outerContext, manager.Current);
            outer.Rollback();
        }

        [Fact]
        public void Begin_Failure_Inside_Suppression_Preserves_Boundary_And_Outer_Root()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            InvalidOperationException beginFailure = new InvalidOperationException("inner begin failed");
            FakeDbConnection failingInnerConnection = new FakeDbConnection
            {
                BeginTransactionException = beginFailure
            };
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, failingInnerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => manager.Begin());

            Assert.Same(beginFailure, thrown);
            Assert.False(manager.HasCurrent);
            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);

            outer.Rollback();
        }

        [Fact]
        public void Independent_Commit_Failure_Does_Not_Lose_Outer_Root()
        {
            InvalidOperationException commitFailure = new InvalidOperationException("inner commit failed");
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection
            {
                CommitException = commitFailure
            };
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => inner.Complete());

            Assert.Same(commitFailure, thrown);
            Assert.False(manager.HasCurrent);
            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            Assert.False(outer.IsRollbackRequested);

            outer.Rollback();
        }

        [Fact]
        public void Independent_Rollback_Failure_Does_Not_Lose_Outer_Root()
        {
            InvalidOperationException rollbackFailure = new InvalidOperationException("inner rollback failed");
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection
            {
                RollbackException = rollbackFailure
            };
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => inner.Rollback());

            Assert.Same(rollbackFailure, thrown);
            Assert.False(manager.HasCurrent);
            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            Assert.False(outer.IsRollbackRequested);

            outer.Rollback();
        }

        [Fact]
        public void Independent_Cleanup_Failure_Does_Not_Lose_Outer_Root()
        {
            InvalidOperationException cleanupFailure = new InvalidOperationException("inner transaction dispose failed");
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection
            {
                TransactionDisposeException = cleanupFailure
            };
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope inner = manager.Begin();

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => inner.Complete());

            Assert.Same(cleanupFailure, thrown);
            Assert.False(manager.HasCurrent);
            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);
            Assert.False(outer.IsRollbackRequested);

            outer.Rollback();
        }

        [Fact]
        public void Suppression_Stacks_Are_Isolated_Per_Manager()
        {
            FakeDbConnection firstConnection = new FakeDbConnection();
            FakeDbConnection secondConnection = new FakeDbConnection();
            UnitOfWorkManager firstManager = new UnitOfWorkManager(() => firstConnection);
            UnitOfWorkManager secondManager = new UnitOfWorkManager(() => secondConnection);
            IUnitOfWorkScope firstOuter = firstManager.Begin();
            IUnitOfWorkScope secondOuter = secondManager.Begin();
            IUnitOfWorkContext firstContext = firstManager.Current;
            IUnitOfWorkContext secondContext = secondManager.Current;
            IDisposable firstSuppression = firstManager.Suppress();
            IDisposable firstNestedSuppression = firstManager.Suppress();

            Assert.False(firstManager.HasCurrent);
            Assert.Same(secondContext, secondManager.Current);

            IDisposable secondSuppression = secondManager.Suppress();
            Assert.False(firstManager.HasCurrent);
            Assert.False(secondManager.HasCurrent);

            firstNestedSuppression.Dispose();
            firstSuppression.Dispose();

            Assert.Same(firstContext, firstManager.Current);
            Assert.False(secondManager.HasCurrent);

            secondSuppression.Dispose();
            Assert.Same(secondContext, secondManager.Current);

            secondOuter.Rollback();
            firstOuter.Rollback();
        }

        [Fact]
        public void Independent_Root_Nested_Scopes_Reuse_Two_Not_Hidden_TOne()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independentOuter = manager.Begin();
            IUnitOfWorkScope independentNested = manager.Begin();

            Assert.NotSame(outer.Db.Transaction, independentOuter.Db.Transaction);
            Assert.Same(independentOuter.Db.Connection, independentNested.Db.Connection);
            Assert.Same(independentOuter.Db.Transaction, independentNested.Db.Transaction);
            Assert.Equal(1, innerConnection.BeginTransactionCallCount);

            independentNested.Complete();
            independentOuter.Complete();
            Assert.False(manager.HasCurrent);

            suppression.Dispose();
            Assert.Same(outer.Db, manager.Current.Db);
            outer.Rollback();
        }

        [Fact]
        public void Inner_Rollback_In_Independent_Root_Dooms_Two_But_Not_TOne()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            FakeDbTransaction outerTransaction = Assert.IsType<FakeDbTransaction>(outerConnection.LastTransaction);
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independentOuter = manager.Begin();
            IUnitOfWorkScope independentNested = manager.Begin();
            FakeDbTransaction innerTransaction = Assert.IsType<FakeDbTransaction>(innerConnection.LastTransaction);

            independentNested.Rollback();
            Assert.True(independentOuter.IsRollbackRequested);
            independentOuter.Complete();

            Assert.Equal(0, innerTransaction.CommitCallCount);
            Assert.Equal(1, innerTransaction.RollbackCallCount);
            Assert.False(manager.HasCurrent);

            suppression.Dispose();
            Assert.False(outer.IsRollbackRequested);

            outer.Complete();
            Assert.Equal(1, outerTransaction.CommitCallCount);
            Assert.Equal(0, outerTransaction.RollbackCallCount);
        }

        [Fact]
        public void Independent_Finalization_Returns_To_Suppressed_State_Before_Outer_Restore()
        {
            FakeDbConnection outerConnection = new FakeDbConnection();
            FakeDbConnection innerConnection = new FakeDbConnection();
            UnitOfWorkManager manager = CreateQueuedManager(outerConnection, innerConnection);
            IUnitOfWorkScope outer = manager.Begin();
            IUnitOfWorkContext outerContext = manager.Current;
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();

            independent.Complete();

            Assert.False(manager.HasCurrent);
            Assert.Throws<UnitOfWorkStateException>(() => manager.Current);

            suppression.Dispose();
            Assert.Same(outerContext, manager.Current);

            outer.Rollback();
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
