using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace NetUnitOfWorkManager.Sample.Net472
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Console.WriteLine("NetUnitOfWorkManager .NET Framework 4.7.2 runtime probe");
                Console.WriteLine($"Target framework: {AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName}");
                Console.WriteLine($"CLR version: {Environment.Version}");

                Run("single scope commit", SingleScopeCommit);
                Run("explicit rollback", ExplicitRollback);
                Run("nested complete", NestedComplete);
                Run("inner rollback forces outer rollback", InnerRollbackForcesOuterRollback);
                Run("async command inside synchronous UoW", AsyncCommandInsideSynchronousScope);
                Run("suppression hides and restores outer root", SuppressionHidesAndRestoresOuterRoot);
                Run("nested suppression restores in LIFO order", NestedSuppressionRestoresInLifoOrder);
                Run("independent fake root inside suppression", IndependentRootInsideSuppression);
                Run("suppression flows across async continuation", SuppressionFlowsAcrossAsyncContinuation);

                Console.WriteLine("All .NET Framework 4.7.2 runtime scenarios passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Runtime compatibility probe failed:");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void SingleScopeCommit()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope scope = manager.Begin())
            {
                ProbeDbConnection currentConnection = RequireConnection(connection);
                ProbeDbTransaction transaction = RequireTransaction(currentConnection.LastTransaction);

                using (DbCommand command = scope.Db.CreateCommand())
                {
                    Expect(ReferenceEquals(command.Connection, currentConnection), "The provider command must use the root connection.");
                    Expect(ReferenceEquals(command.Transaction, transaction), "The provider command must be bound to the root transaction.");
                    Expect(command.ExecuteNonQuery() == 1, "The synchronous provider command should execute successfully.");
                }

                scope.Complete();

                Expect(transaction.CommitCallCount == 1, "Single scope completion must commit exactly once.");
                Expect(transaction.RollbackCallCount == 0, "Single scope completion must not rollback.");
            }

            Expect(!manager.HasCurrent, "Ambient state must be cleared after commit.");
        }

        private static void ExplicitRollback()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope scope = manager.Begin())
            {
                ProbeDbTransaction transaction = RequireTransaction(RequireConnection(connection).LastTransaction);

                scope.Rollback();

                Expect(transaction.CommitCallCount == 0, "Explicit rollback must not commit.");
                Expect(transaction.RollbackCallCount == 1, "Explicit rollback must rollback exactly once.");
            }

            Expect(!manager.HasCurrent, "Ambient state must be cleared after rollback.");
        }

        private static void NestedComplete()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope outer = manager.Begin())
            {
                ProbeDbConnection currentConnection = RequireConnection(connection);
                ProbeDbTransaction transaction = RequireTransaction(currentConnection.LastTransaction);

                using (IUnitOfWorkScope inner = manager.Begin())
                {
                    Expect(ReferenceEquals(outer.Db.Connection, inner.Db.Connection), "Nested scopes must share one physical connection.");
                    Expect(ReferenceEquals(outer.Db.Transaction, inner.Db.Transaction), "Nested scopes must share one physical transaction.");

                    inner.Complete();
                    Expect(transaction.CommitCallCount == 0, "Inner completion must not commit the physical transaction.");
                }

                outer.Complete();
                Expect(transaction.CommitCallCount == 1, "Outer completion must commit after all nested scopes complete.");
                Expect(transaction.RollbackCallCount == 0, "Successful nested completion must not rollback.");
            }
        }

        private static void InnerRollbackForcesOuterRollback()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope outer = manager.Begin())
            {
                ProbeDbTransaction transaction = RequireTransaction(RequireConnection(connection).LastTransaction);

                using (IUnitOfWorkScope inner = manager.Begin())
                {
                    inner.Rollback();
                    Expect(outer.IsRollbackRequested, "An inner rollback must mark the root rollback-only.");
                    Expect(transaction.RollbackCallCount == 0, "Physical rollback must wait for the outer scope to settle.");
                }

                outer.Complete();
                Expect(transaction.CommitCallCount == 0, "Rollback-only root must never commit.");
                Expect(transaction.RollbackCallCount == 1, "Rollback-only root must rollback exactly once when the outer scope settles.");
            }
        }

        private static void AsyncCommandInsideSynchronousScope()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope scope = manager.Begin())
            {
                ProbeDbConnection currentConnection = RequireConnection(connection);
                ProbeDbTransaction transaction = RequireTransaction(currentConnection.LastTransaction);

                using (DbCommand command = scope.Db.CreateCommand())
                {
                    Expect(command is ProbeDbCommand, "CreateCommand must return the provider-native command type.");
                    Expect(ReferenceEquals(command.Transaction, transaction), "The async command must remain bound to the synchronous UoW transaction.");

                    int affectedRows = command.ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Expect(affectedRows == 1, "The provider async command should execute successfully.");
                    Expect(((ProbeDbCommand)command).AsyncExecuteNonQueryCallCount == 1, "The provider async override must be invoked exactly once.");
                }

                scope.Complete();
                Expect(transaction.CommitCallCount == 1, "The synchronous UoW lifecycle must commit after async command execution.");
            }
        }

        private static void SuppressionHidesAndRestoresOuterRoot()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope outer = manager.Begin())
            {
                IUnitOfWorkContext outerContext = manager.Current;
                ProbeDbTransaction outerTransaction = RequireTransaction(RequireConnection(connection).LastTransaction);

                using (manager.Suppress())
                {
                    Expect(!manager.HasCurrent, "Suppression must hide the outer ambient root.");
                    ExpectThrows<UnitOfWorkStateException>(
                        () => { IUnitOfWorkContext _ = manager.Current; },
                        "Current must throw while the ambient root is suppressed.");
                    Expect(outerTransaction.CommitCallCount == 0, "Suppress() must not commit the outer transaction.");
                    Expect(outerTransaction.RollbackCallCount == 0, "Suppress() must not rollback the outer transaction.");
                }

                Expect(manager.HasCurrent, "Disposing suppression must restore ambient visibility.");
                Expect(ReferenceEquals(manager.Current, outerContext), "Suppression must restore the exact outer root context.");
                outer.Rollback();
            }
        }

        private static void NestedSuppressionRestoresInLifoOrder()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope outer = manager.Begin())
            {
                IUnitOfWorkContext outerContext = manager.Current;

                using (manager.Suppress())
                {
                    Expect(!manager.HasCurrent, "First suppression must hide the outer root.");

                    using (manager.Suppress())
                    {
                        Expect(!manager.HasCurrent, "Nested suppression must keep the outer root hidden.");
                    }

                    Expect(!manager.HasCurrent, "Disposing nested suppression must restore the first suppression boundary.");
                }

                Expect(ReferenceEquals(manager.Current, outerContext), "LIFO suppression disposal must restore the exact outer root.");
                outer.Rollback();
            }
        }

        private static void IndependentRootInsideSuppression()
        {
            ProbeDbConnection? outerConnection = null;
            ProbeDbConnection? independentConnection = null;
            int connectionCount = 0;
            UnitOfWorkManager manager = new UnitOfWorkManager(() =>
            {
                ProbeDbConnection created = new ProbeDbConnection();
                if (connectionCount++ == 0)
                {
                    outerConnection = created;
                }
                else
                {
                    independentConnection = created;
                }

                return created;
            });

            using (IUnitOfWorkScope outer = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable)))
            {
                IUnitOfWorkContext outerContext = manager.Current;
                ProbeDbTransaction outerTransaction = RequireTransaction(RequireConnection(outerConnection).LastTransaction);

                using (manager.Suppress())
                {
                    using (IUnitOfWorkScope independent = manager.Begin(new UnitOfWorkOptions(IsolationLevel.ReadCommitted)))
                    {
                        ProbeDbConnection currentIndependentConnection = RequireConnection(independentConnection);
                        ProbeDbTransaction independentTransaction = RequireTransaction(currentIndependentConnection.LastTransaction);

                        Expect(!ReferenceEquals(outer.Db.Connection, independent.Db.Connection), "Independent root must use a different physical connection.");
                        Expect(!ReferenceEquals(outer.Db.Transaction, independent.Db.Transaction), "Independent root must use a different physical transaction.");
                        Expect(independentTransaction.IsolationLevel == IsolationLevel.ReadCommitted, "Independent root may use a different isolation level.");

                        independent.Complete();
                        Expect(independentTransaction.CommitCallCount == 1, "Independent root must commit independently.");
                    }

                    Expect(!manager.HasCurrent, "Independent root finalization must return to the suppression boundary.");
                }

                Expect(ReferenceEquals(manager.Current, outerContext), "Disposing suppression must restore the exact outer context.");
                outer.Rollback();
                Expect(outerTransaction.RollbackCallCount == 1, "Outer root must still be able to rollback after independent commit.");
            }
        }

        private static void SuppressionFlowsAcrossAsyncContinuation()
        {
            SuppressionFlowsAcrossAsyncContinuationAsync().GetAwaiter().GetResult();
        }

        private static async Task SuppressionFlowsAcrossAsyncContinuationAsync()
        {
            ProbeDbConnection? connection = null;
            UnitOfWorkManager manager = new UnitOfWorkManager(() => connection = new ProbeDbConnection());

            using (IUnitOfWorkScope outer = manager.Begin())
            {
                IUnitOfWorkContext outerContext = manager.Current;

                using (manager.Suppress())
                {
                    Expect(!manager.HasCurrent, "Suppression must be visible before the async continuation.");
                    await Task.Yield();
                    Expect(!manager.HasCurrent, "Suppression must flow across await via AsyncLocal semantics.");

                    using (IUnitOfWorkScope independent = manager.Begin())
                    {
                        using (DbCommand command = independent.Db.CreateCommand())
                        {
                            int affectedRows = await command.ExecuteNonQueryAsync(CancellationToken.None);
                            Expect(affectedRows == 1, "The independent provider async command should execute successfully.");
                        }

                        independent.Complete();
                    }

                    Expect(!manager.HasCurrent, "Independent async root finalization must return to suppressed state.");
                    await Task.Yield();
                    Expect(!manager.HasCurrent, "Suppressed state must remain stable across a later continuation.");
                }

                Expect(ReferenceEquals(manager.Current, outerContext), "Outer ambient root must be restored after async suppression cleanup.");
                outer.Complete();
            }
        }

        private static void Run(string name, Action scenario)
        {
            scenario();
            Console.WriteLine($"PASS: {name}");
        }

        private static ProbeDbConnection RequireConnection(ProbeDbConnection? connection)
        {
            return connection ?? throw new InvalidOperationException("The provider connection was not created.");
        }

        private static ProbeDbTransaction RequireTransaction(ProbeDbTransaction? transaction)
        {
            return transaction ?? throw new InvalidOperationException("The provider transaction was not created.");
        }

        private static void ExpectThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    internal sealed class ProbeDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        private string _connectionString = string.Empty;

        internal ProbeDbTransaction? LastTransaction { get; private set; }

        internal ProbeDbCommand? LastCommand { get; private set; }

        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "RuntimeProbe";

        public override string DataSource => "InProcessProvider";

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
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            ProbeDbTransaction transaction = new ProbeDbTransaction(this, isolationLevel);
            LastTransaction = transaction;
            return transaction;
        }

        protected override DbCommand CreateDbCommand()
        {
            ProbeDbCommand command = new ProbeDbCommand(this);
            LastCommand = command;
            return command;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _state = ConnectionState.Closed;
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class ProbeDbTransaction : DbTransaction
    {
        private readonly DbConnection _connection;

        internal ProbeDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            IsolationLevel = isolationLevel;
        }

        internal int CommitCallCount { get; private set; }

        internal int RollbackCallCount { get; private set; }

        public override IsolationLevel IsolationLevel { get; }

        protected override DbConnection DbConnection => _connection;

        public override void Commit()
        {
            CommitCallCount++;
        }

        public override void Rollback()
        {
            RollbackCallCount++;
        }
    }

    internal sealed class ProbeDbCommand : DbCommand
    {
        private readonly ProbeDbConnection _connection;
        private DbTransaction? _transaction;
        private string _commandText = string.Empty;

        internal ProbeDbCommand(ProbeDbConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        internal int AsyncExecuteNonQueryCallCount { get; private set; }

        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set
            {
                if (!ReferenceEquals(value, _connection))
                {
                    throw new NotSupportedException("ProbeDbCommand cannot be moved to another connection.");
                }
            }
        }

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

        protected override DbTransaction? DbTransaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            EnsureTransactionBound();
            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureTransactionBound();
            AsyncExecuteNonQueryCallCount++;
            return Task.FromResult(1);
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            throw new NotSupportedException();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        private void EnsureTransactionBound()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("The runtime probe command is not transaction-bound.");
            }
        }
    }
}
