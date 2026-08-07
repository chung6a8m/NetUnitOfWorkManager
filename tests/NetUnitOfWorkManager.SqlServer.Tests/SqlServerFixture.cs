using System;
using System.Data;
using Microsoft.Data.SqlClient;
using RepoDb;
using Xunit;

namespace NetUnitOfWorkManager.SqlServer.Tests
{
    [CollectionDefinition(Name)]
    public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "SQL Server integration";
    }

    public sealed class SqlServerFixture : IDisposable
    {
        public const string ConnectionStringEnvironmentVariable = "NETUOW_SQLSERVER_CONNECTION_STRING";

        private static readonly object RepoDbInitializationLock = new object();
        private static bool _repoDbInitialized;
        private bool _disposed;

        public SqlServerFixture()
        {
            string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Set {ConnectionStringEnvironmentVariable} to a SQL Server database connection string before running P08 integration tests.");
            }

            ConnectionString = connectionString;
            TableName = "NetUowP08_" + Guid.NewGuid().ToString("N");

            EnsureRepoDbInitialized();
            CreateTable();
        }

        public string ConnectionString { get; }

        public string TableName { get; }

        public UnitOfWorkManager CreateManager()
        {
            return new UnitOfWorkManager(() => new SqlConnection(ConnectionString));
        }

        public string CreateTestKey(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N");
        }

        public int CountRows(string testKey)
        {
            using (SqlConnection connection = OpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT COUNT(*) FROM [dbo].[{TableName}] WHERE [TestKey] = @TestKey;";
                command.Parameters.Add("@TestKey", SqlDbType.NVarChar, 96).Value = testKey;
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public string? ReadValue(string testKey)
        {
            using (SqlConnection connection = OpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT [Value] FROM [dbo].[{TableName}] WHERE [TestKey] = @TestKey;";
                command.Parameters.Add("@TestKey", SqlDbType.NVarChar, 96).Value = testKey;
                object? value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            using (SqlConnection connection = OpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $"IF OBJECT_ID(N'[dbo].[{TableName}]', N'U') IS NOT NULL DROP TABLE [dbo].[{TableName}];";
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureRepoDbInitialized()
        {
            lock (RepoDbInitializationLock)
            {
                if (_repoDbInitialized)
                {
                    return;
                }

                GlobalConfiguration.Setup().UseSqlServer();
                _repoDbInitialized = true;
            }
        }

        private SqlConnection OpenConnection()
        {
            SqlConnection connection = new SqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        private void CreateTable()
        {
            using (SqlConnection connection = OpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $@"
CREATE TABLE [dbo].[{TableName}]
(
    [Id] INT IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_{TableName}] PRIMARY KEY,
    [TestKey] NVARCHAR(96) NOT NULL,
    [Value] NVARCHAR(256) NOT NULL
);";
                command.ExecuteNonQuery();
            }
        }
    }

    internal sealed class IntegrationRow
    {
        public int Id { get; set; }

        public string TestKey { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}
