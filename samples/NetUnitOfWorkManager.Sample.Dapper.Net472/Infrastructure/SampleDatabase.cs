using System;
using System.Data.Common;
using System.Data.SqlClient;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472.Infrastructure
{
    public sealed class SampleDatabase
    {
        public const string TableName = "[dbo].[NetUnitOfWorkCounter]";

        private readonly string _connectionString;

        public SampleDatabase(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
            }

            _connectionString = connectionString;
        }

        public DbConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }

        public void EnsureCreated()
        {
            const string sql = @"
IF OBJECT_ID(N'[dbo].[NetUnitOfWorkCounter]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NetUnitOfWorkCounter]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT [PK_NetUnitOfWorkCounter] PRIMARY KEY,
        [Value] INT NOT NULL
    );
END;";

            ExecuteNonQuery(sql);
        }

        public void Reset()
        {
            ExecuteNonQuery("DELETE FROM " + TableName + ";");
        }

        private void ExecuteNonQuery(string sql)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
