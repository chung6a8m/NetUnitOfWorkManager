using System;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using RepoDb;
using Xunit;

namespace NetUnitOfWorkManager.SqlServer.Tests
{
    [Collection(SqlServerCollection.Name)]
    public sealed class SuppressionIntegrationTests
    {
        private readonly SqlServerFixture _fixture;

        public SuppressionIntegrationTests(SqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Independent_Commit_Survives_Outer_Rollback()
        {
            string independentKey = _fixture.CreateTestKey("suppress_independent_commit");
            string outerKey = _fixture.CreateTestKey("suppress_outer_rollback");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();

            InsertWithAdoNet(independent, independentKey, "independent committed");
            independent.Complete();

            Assert.False(manager.HasCurrent);
            suppression.Dispose();

            InsertWithAdoNet(outer, outerKey, "outer rolled back");
            outer.Rollback();

            Assert.Equal("independent committed", _fixture.ReadValue(independentKey));
            Assert.Equal(0, _fixture.CountRows(outerKey));
        }

        [Fact]
        public void Independent_Rollback_Does_Not_Prevent_Outer_Commit()
        {
            string independentKey = _fixture.CreateTestKey("suppress_independent_rollback");
            string outerKey = _fixture.CreateTestKey("suppress_outer_commit");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();

            InsertWithAdoNet(independent, independentKey, "independent rolled back");
            independent.Rollback();

            Assert.False(manager.HasCurrent);
            suppression.Dispose();

            InsertWithAdoNet(outer, outerKey, "outer committed");
            outer.Complete();

            Assert.Equal(0, _fixture.CountRows(independentKey));
            Assert.Equal("outer committed", _fixture.ReadValue(outerKey));
        }

        [Fact]
        public void Independent_Transaction_Uses_Different_Connection()
        {
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin();
            SqlConnection outerConnection = Assert.IsType<SqlConnection>(outer.Db.Connection);
            SqlTransaction outerTransaction = Assert.IsType<SqlTransaction>(outer.Db.Transaction);
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();
            SqlConnection independentConnection = Assert.IsType<SqlConnection>(independent.Db.Connection);
            SqlTransaction independentTransaction = Assert.IsType<SqlTransaction>(independent.Db.Transaction);

            Assert.NotSame(outerConnection, independentConnection);
            Assert.NotSame(outerTransaction, independentTransaction);
            Assert.NotEqual(outerConnection.ClientConnectionId, independentConnection.ClientConnectionId);

            independent.Rollback();
            suppression.Dispose();
            outer.Rollback();
        }

        [Fact]
        public void Independent_Transaction_Can_Use_Different_IsolationLevel()
        {
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable));
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin(new UnitOfWorkOptions(IsolationLevel.ReadCommitted));

            Assert.Equal(IsolationLevel.Serializable, outer.Db.Transaction.IsolationLevel);
            Assert.Equal(IsolationLevel.ReadCommitted, independent.Db.Transaction.IsolationLevel);

            independent.Rollback();
            suppression.Dispose();
            outer.Rollback();
        }

        [Fact]
        public void Dapper_Independent_Commit_Survives_Outer_Rollback()
        {
            string independentKey = _fixture.CreateTestKey("dapper_suppress_independent");
            string outerKey = _fixture.CreateTestKey("dapper_suppress_outer");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();

            Assert.Equal(
                1,
                independent.Db.Connection.Execute(
                    $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);",
                    new { TestKey = independentKey, Value = "dapper independent committed" },
                    independent.Db.Transaction));
            independent.Complete();

            Assert.False(manager.HasCurrent);
            suppression.Dispose();

            Assert.Equal(
                1,
                outer.Db.Connection.Execute(
                    $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);",
                    new { TestKey = outerKey, Value = "dapper outer rolled back" },
                    outer.Db.Transaction));
            outer.Rollback();

            Assert.Equal("dapper independent committed", _fixture.ReadValue(independentKey));
            Assert.Equal(0, _fixture.CountRows(outerKey));
        }

        [Fact]
        public void RepoDb_Independent_Commit_Survives_Outer_Rollback()
        {
            string independentKey = _fixture.CreateTestKey("repodb_suppress_independent");
            string outerKey = _fixture.CreateTestKey("repodb_suppress_outer");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope outer = manager.Begin();
            IDisposable suppression = manager.Suppress();
            IUnitOfWorkScope independent = manager.Begin();
            SqlConnection independentConnection = Assert.IsType<SqlConnection>(independent.Db.Connection);
            SqlTransaction independentTransaction = Assert.IsType<SqlTransaction>(independent.Db.Transaction);
            IntegrationRow independentRow = new IntegrationRow
            {
                TestKey = independentKey,
                Value = "repodb independent committed"
            };

            independentConnection.Insert(
                _fixture.TableName,
                independentRow,
                transaction: independentTransaction);
            independent.Complete();

            Assert.False(manager.HasCurrent);
            suppression.Dispose();

            SqlConnection outerConnection = Assert.IsType<SqlConnection>(outer.Db.Connection);
            SqlTransaction outerTransaction = Assert.IsType<SqlTransaction>(outer.Db.Transaction);
            IntegrationRow outerRow = new IntegrationRow
            {
                TestKey = outerKey,
                Value = "repodb outer rolled back"
            };

            outerConnection.Insert(
                _fixture.TableName,
                outerRow,
                transaction: outerTransaction);
            outer.Rollback();

            Assert.Equal("repodb independent committed", _fixture.ReadValue(independentKey));
            Assert.Equal(0, _fixture.CountRows(outerKey));
        }

        private void InsertWithAdoNet(IUnitOfWorkScope scope, string testKey, string value)
        {
            using (SqlCommand command = Assert.IsType<SqlCommand>(scope.Db.CreateCommand()))
            {
                command.CommandText = $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);";
                command.Parameters.Add("@TestKey", SqlDbType.NVarChar, 96).Value = testKey;
                command.Parameters.Add("@Value", SqlDbType.NVarChar, 256).Value = value;
                Assert.Equal(1, command.ExecuteNonQuery());
            }
        }
    }
}
