using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace NetUnitOfWorkManager.SqlServer.Tests
{
    [Collection(SqlServerCollection.Name)]
    public sealed class AdoNetIntegrationTests
    {
        private readonly SqlServerFixture _fixture;

        public AdoNetIntegrationTests(SqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void CreateCommand_Rollback_Actually_Undoes_Data()
        {
            string testKey = _fixture.CreateTestKey("ado_rollback");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();

            using (SqlCommand command = Assert.IsType<SqlCommand>(scope.Db.CreateCommand()))
            {
                command.CommandText = $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);";
                command.Parameters.Add("@TestKey", SqlDbType.NVarChar, 96).Value = testKey;
                command.Parameters.Add("@Value", SqlDbType.NVarChar, 256).Value = "ado rollback";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            scope.Rollback();

            Assert.Equal(0, _fixture.CountRows(testKey));
        }

        [Fact]
        public async Task SqlCommand_ExecuteNonQueryAsync_Works_Inside_Synchronous_Scope()
        {
            string testKey = _fixture.CreateTestKey("ado_async");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();

            using (SqlCommand command = Assert.IsType<SqlCommand>(scope.Db.CreateCommand()))
            {
                command.CommandText = $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);";
                command.Parameters.Add("@TestKey", SqlDbType.NVarChar, 96).Value = testKey;
                command.Parameters.Add("@Value", SqlDbType.NVarChar, 256).Value = "ado async";
                Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }

            scope.Complete();

            Assert.Equal(1, _fixture.CountRows(testKey));
        }

        [Fact]
        public void Requested_IsolationLevel_Is_Applied_To_Provider_Transaction()
        {
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin(new UnitOfWorkOptions(IsolationLevel.Serializable));

            SqlTransaction transaction = Assert.IsType<SqlTransaction>(scope.Db.Transaction);

            Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);

            scope.Rollback();
        }
    }
}
