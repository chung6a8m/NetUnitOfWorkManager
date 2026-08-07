using NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Services
{
    public sealed class NestedCounterService
    {
        private readonly global::NetUnitOfWorkManager.IUnitOfWorkManager _unitOfWorkManager;
        private readonly ICounterRepository _counterRepository;

        public NestedCounterService(
            global::NetUnitOfWorkManager.IUnitOfWorkManager unitOfWorkManager,
            ICounterRepository counterRepository)
        {
            _unitOfWorkManager = unitOfWorkManager;
            _counterRepository = counterRepository;
        }

        public void InsertAndComplete(
            int value,
            global::NetUnitOfWorkManager.UnitOfWorkOptions options)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(options))
            {
                _counterRepository.Insert(value);
                scope.Complete();
            }
        }

        public void InsertWithoutCompleting(
            int value,
            global::NetUnitOfWorkManager.UnitOfWorkOptions options)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(options))
            {
                _counterRepository.Insert(value);
            }
        }
    }
}
