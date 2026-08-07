using System.Collections.Generic;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Models;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories
{
    public interface ICounterRepository
    {
        void Insert(int value);

        IReadOnlyList<CounterItem> List();
    }
}
