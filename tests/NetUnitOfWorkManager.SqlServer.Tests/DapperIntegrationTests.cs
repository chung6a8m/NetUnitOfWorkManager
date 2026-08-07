using System.Threading.Tasks;
using Dapper;
using Xunit;

namespace NetUnitOfWorkManager.SqlServer.Tests
{
    [Collection(SqlServerCollection.Name)]
    public sealed class DapperIntegrationTests
    {
        private readonly SqlServerFixture _fixture;

        public DapperIntegrationTests(SqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Execute_And_Query_Use_The_UnitOfWork_Transaction()
        {
            string testKey = _fixture.CreateTestKey("dapper_sync");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();

            int affectedRows = scope.Db.Connection.Execute(
                $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);",
                new { TestKey = testKey, Value = "dapper sync" },
                scope.Db.Transaction);

            string value = scope.Db.Connection.QuerySingle<string>(
                $"SELECT [Value] FROM [dbo].[{_fixture.TableName}] WHERE [TestKey] = @TestKey;",
                new { TestKey = testKey },
                scope.Db.Transaction);

            Assert.Equal(1, affectedRows);
            Assert.Equal("dapper sync", value);

            scope.Complete();

            Assert.Equal("dapper sync", _fixture.ReadValue(testKey));
        }

        [Fact]
        public void Rollback_Actually_Undoes_Dapper_Data()
        {
            string testKey = _fixture.CreateTestKey("dapper_rollback");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();

            Assert.Equal(
                1,
                scope.Db.Connection.Execute(
                    $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);",
                    new { TestKey = testKey, Value = "dapper rollback" },
                    scope.Db.Transaction));

            scope.Rollback();

            Assert.Equal(0, _fixture.CountRows(testKey));
        }

        [Fact]
        public async Task ExecuteAsync_Does_Not_Require_Async_UnitOfWork_Lifecycle()
        {
            string testKey = _fixture.CreateTestKey("dapper_async");
            UnitOfWorkManager manager = _fixture.CreateManager();
            IUnitOfWorkScope scope = manager.Begin();

            int affectedRows = await scope.Db.Connection.ExecuteAsync(
                $"INSERT INTO [dbo].[{_fixture.TableName}] ([TestKey], [Value]) VALUES (@TestKey, @Value);",
                new { TestKey = testKey, Value = "dapper async" },
                scope.Db.Transaction);

            string value = await scope.Db.Connection.QuerySingleAsync<string>(
                $"SELECT [Value] FROM [dbo].[{_fixture.TableName}] WHERE [TestKey] = @TestKey;",
                new { TestKey = testKey },
                scope.Db.Transaction);

            Assert.Equal(1, affectedRows);
            Assert.Equal("dapper async", value);

            scope.Complete();

            Assert.Equal("dapper async", _fixture.ReadValue(testKey));
        }
    }
}
