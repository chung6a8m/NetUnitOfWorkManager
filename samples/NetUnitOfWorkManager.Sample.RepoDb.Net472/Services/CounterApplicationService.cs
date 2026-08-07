using System.Collections.Generic;
using System.Data;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Models;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Services
{
    public sealed class CounterApplicationService
    {
        private static readonly global::NetUnitOfWorkManager.UnitOfWorkOptions Options =
            new global::NetUnitOfWorkManager.UnitOfWorkOptions(IsolationLevel.Serializable);

        private readonly global::NetUnitOfWorkManager.IUnitOfWorkManager _unitOfWorkManager;
        private readonly ICounterRepository _counterRepository;
        private readonly NestedCounterService _nestedCounterService;

        public CounterApplicationService(
            global::NetUnitOfWorkManager.IUnitOfWorkManager unitOfWorkManager,
            ICounterRepository counterRepository,
            NestedCounterService nestedCounterService)
        {
            _unitOfWorkManager = unitOfWorkManager;
            _counterRepository = counterRepository;
            _nestedCounterService = nestedCounterService;
        }

        public IReadOnlyList<CounterItem> List()
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(Options))
            {
                IReadOnlyList<CounterItem> items = _counterRepository.List();
                scope.Complete();
                return items;
            }
        }

        public void CommitPair(int outerValue, int innerValue)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(Options))
            {
                _counterRepository.Insert(outerValue);
                _nestedCounterService.InsertAndComplete(innerValue, Options);
                scope.Complete();
            }
        }

        public void RollbackPair(int outerValue, int innerValue)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(Options))
            {
                _counterRepository.Insert(outerValue);
                _nestedCounterService.InsertWithoutCompleting(innerValue, Options);
                scope.Complete();
            }
        }
    }
}
