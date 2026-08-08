using System.Collections.Generic;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Models;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472.Repositories
{
    public interface ICounterRepository
    {
        void Insert(int value);

        IReadOnlyList<CounterItem> List();
    }
}
