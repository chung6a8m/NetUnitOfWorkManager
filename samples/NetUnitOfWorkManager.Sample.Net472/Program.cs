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
