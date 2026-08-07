using System.Collections.Generic;
using System.Linq;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Models;
using RepoDb;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories
{
    public sealed class RepoDbCounterRepository : ICounterRepository
    {
        private readonly global::NetUnitOfWorkManager.IUnitOfWorkManager _unitOfWorkManager;

        public RepoDbCounterRepository(
            global::NetUnitOfWorkManager.IUnitOfWorkManager unitOfWorkManager)
        {
            _unitOfWorkManager = unitOfWorkManager;
        }

        public void Insert(int value)
        {
            global::NetUnitOfWorkManager.UnitOfWorkDbSession db = _unitOfWorkManager.Current.Db;

            db.Connection.ExecuteNonQuery(
                "INSERT INTO " + SqlServerSampleDatabase.TableName + " ([Value]) VALUES (@Value);",
                new { Value = value },
                transaction: db.Transaction);
        }

        public IReadOnlyList<CounterItem> List()
        {
            global::NetUnitOfWorkManager.UnitOfWorkDbSession db = _unitOfWorkManager.Current.Db;

            return db.Connection.ExecuteQuery<CounterItem>(
                    "SELECT [Id], [Value] FROM " + SqlServerSampleDatabase.TableName + " ORDER BY [Id];",
                    transaction: db.Transaction)
                .ToArray();
        }
    }
}
