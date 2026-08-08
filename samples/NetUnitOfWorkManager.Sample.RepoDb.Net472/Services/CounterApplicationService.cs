using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Models;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472.Services
{
    public sealed class CounterApplicationService
    {
        private static readonly global::NetUnitOfWorkManager.UnitOfWorkOptions Options =
            new global::NetUnitOfWorkManager.UnitOfWorkOptions(IsolationLevel.Serializable);

        private static readonly global::NetUnitOfWorkManager.UnitOfWorkOptions IndependentOptions =
            new global::NetUnitOfWorkManager.UnitOfWorkOptions(IsolationLevel.ReadCommitted);

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

        public void CommitIndependentInsideSuppressionThenRollbackOuter(
            int outerValue,
            int independentValue)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope outer =
                _unitOfWorkManager.Begin(Options))
            {
                DbConnection outerConnection = outer.Db.Connection;
                DbTransaction outerTransaction = outer.Db.Transaction;
                _counterRepository.Insert(outerValue);

                using (_unitOfWorkManager.Suppress())
                {
                    Expect(!_unitOfWorkManager.HasCurrent, "Suppression must hide the outer RepoDb root.");

                    using (global::NetUnitOfWorkManager.IUnitOfWorkScope independent =
                        _unitOfWorkManager.Begin(IndependentOptions))
                    {
                        Expect(
                            !ReferenceEquals(outerConnection, independent.Db.Connection),
                            "Suppress() + Begin() must create a different RepoDb connection.");
                        Expect(
                            !ReferenceEquals(outerTransaction, independent.Db.Transaction),
                            "Suppress() + Begin() must create a different RepoDb transaction.");

                        _counterRepository.Insert(independentValue);
                        independent.Complete();
                    }

                    Expect(
                        !_unitOfWorkManager.HasCurrent,
                        "After independent RepoDb root finalization the suppression boundary must remain active.");
                }

                Expect(_unitOfWorkManager.HasCurrent, "Disposing suppression must restore the outer RepoDb root.");
                Expect(
                    ReferenceEquals(outerConnection, _unitOfWorkManager.Current.Db.Connection),
                    "Suppression must restore the exact outer RepoDb connection.");
                Expect(
                    ReferenceEquals(outerTransaction, _unitOfWorkManager.Current.Db.Transaction),
                    "Suppression must restore the exact outer RepoDb transaction.");

                outer.Rollback();
            }
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
