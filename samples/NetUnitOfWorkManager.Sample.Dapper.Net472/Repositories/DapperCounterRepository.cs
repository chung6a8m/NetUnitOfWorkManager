using System.Collections.Generic;
using System.Linq;
using Dapper;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Models;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472.Repositories
{
    public sealed class DapperCounterRepository : ICounterRepository
    {
        private readonly global::NetUnitOfWorkManager.IUnitOfWorkManager _unitOfWorkManager;

        public DapperCounterRepository(
            global::NetUnitOfWorkManager.IUnitOfWorkManager unitOfWorkManager)
        {
            _unitOfWorkManager = unitOfWorkManager;
        }

        public void Insert(int value)
        {
            global::NetUnitOfWorkManager.UnitOfWorkDbSession db = _unitOfWorkManager.Current.Db;

            db.Connection.Execute(
                "INSERT INTO " + SampleDatabase.TableName + " ([Value]) VALUES (@Value);",
                new { Value = value },
                transaction: db.Transaction);
        }

        public IReadOnlyList<CounterItem> List()
        {
            global::NetUnitOfWorkManager.UnitOfWorkDbSession db = _unitOfWorkManager.Current.Db;

            return db.Connection.Query<CounterItem>(
                    "SELECT [Id], [Value] FROM " + SampleDatabase.TableName + " ORDER BY [Id];",
                    transaction: db.Transaction)
                .ToArray();
        }
    }
}
