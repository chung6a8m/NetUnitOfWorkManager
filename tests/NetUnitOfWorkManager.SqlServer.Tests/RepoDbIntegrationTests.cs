using System;
using Microsoft.Data.SqlClient;
using RepoDb;
using Xunit;

namespace NetUnitOfWorkManager.SqlServer.Tests
{
    [Collection(SqlServerCollection.Name)]
    public sealed class RepoDbIntegrationTests
    {
        private readonly SqlServerFixture _fixture;

        public RepoDbIntegrationTests(SqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void ProviderNative_Connection_And_Transaction_Support_Insert_And_Update()
        {
            string testKey = _fixture.CreateTestKey("repodb_commit");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();
            SqlConnection connection = Assert.IsType<SqlConnection>(scope.Db.Connection);
            SqlTransaction transaction = Assert.IsType<SqlTransaction>(scope.Db.Transaction);
            IntegrationRow row = new IntegrationRow
            {
                TestKey = testKey,
                Value = "repodb inserted"
            };

            object identity = connection.Insert(
                _fixture.TableName,
                row,
                transaction: transaction);
            row.Id = Convert.ToInt32(identity);
            row.Value = "repodb updated";

            int affectedRows = connection.Update(
                _fixture.TableName,
                row,
                transaction: transaction);

            Assert.Equal(1, affectedRows);

            scope.Complete();

            Assert.Equal("repodb updated", _fixture.ReadValue(testKey));
        }

        [Fact]
        public void Rollback_Actually_Undoes_RepoDb_Insert_And_Update()
        {
            string testKey = _fixture.CreateTestKey("repodb_rollback");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();
            SqlConnection connection = Assert.IsType<SqlConnection>(scope.Db.Connection);
            SqlTransaction transaction = Assert.IsType<SqlTransaction>(scope.Db.Transaction);
            IntegrationRow row = new IntegrationRow
            {
                TestKey = testKey,
                Value = "repodb inserted"
            };

            object identity = connection.Insert(
                _fixture.TableName,
                row,
                transaction: transaction);
            row.Id = Convert.ToInt32(identity);
            row.Value = "repodb updated before rollback";

            Assert.Equal(
                1,
                connection.Update(
                    _fixture.TableName,
                    row,
                    transaction: transaction));

            scope.Rollback();

            Assert.Equal(0, _fixture.CountRows(testKey));
        }
    }
}
