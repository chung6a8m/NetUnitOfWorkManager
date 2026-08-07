using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace NetUnitOfWorkManager.Tests.Fakes
{
    internal sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private DbTransaction? _transaction;
        private string _commandText = string.Empty;

        internal FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

#if NET8_0_OR_GREATER
        [AllowNull]
#endif
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
                    throw new NotSupportedException("FakeDbCommand cannot be moved to another connection.");
                }
            }
        }

        protected override DbParameterCollection DbParameterCollection =>
            throw new NotSupportedException();

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
            throw new NotSupportedException();
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
    }
}
