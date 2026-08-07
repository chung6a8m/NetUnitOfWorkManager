using System.Collections.Generic;
using System.Linq;
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

            db.Connection.Insert<CounterItem, long>(
                new CounterItem
                {
                    Value = value
                },
                transaction: db.Transaction);
        }

        public IReadOnlyList<CounterItem> List()
        {
            global::NetUnitOfWorkManager.UnitOfWorkDbSession db = _unitOfWorkManager.Current.Db;

            return db.Connection.QueryAll<CounterItem>(
                    orderBy: OrderField.Parse(new
                    {
                        Id = Order.Ascending
                    }),
                    transaction: db.Transaction)
                .ToArray();
        }
    }
}
