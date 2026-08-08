using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Models;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Repositories;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472.Services
{
    public sealed class CounterService
    {
        private static readonly global::NetUnitOfWorkManager.UnitOfWorkOptions SerializableOptions =
            new global::NetUnitOfWorkManager.UnitOfWorkOptions(IsolationLevel.Serializable);

        private static readonly global::NetUnitOfWorkManager.UnitOfWorkOptions IndependentOptions =
            new global::NetUnitOfWorkManager.UnitOfWorkOptions(IsolationLevel.ReadCommitted);

        private readonly global::NetUnitOfWorkManager.IUnitOfWorkManager _unitOfWorkManager;
        private readonly ICounterRepository _counterRepository;

        public CounterService(
            global::NetUnitOfWorkManager.IUnitOfWorkManager unitOfWorkManager,
            ICounterRepository counterRepository)
        {
            _unitOfWorkManager = unitOfWorkManager;
            _counterRepository = counterRepository;
        }

        public IReadOnlyList<CounterItem> List()
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(SerializableOptions))
            {
                IReadOnlyList<CounterItem> items = _counterRepository.List();
                scope.Complete();
                return items;
            }
        }

        public void CommitSingle(int value)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(SerializableOptions))
            {
                _counterRepository.Insert(value);
                scope.Complete();
            }
        }

        public void RollbackSingle(int value)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope scope =
                _unitOfWorkManager.Begin(SerializableOptions))
            {
                _counterRepository.Insert(value);
                scope.Rollback();
            }
        }

        public void CommitNestedPair(int outerValue, int innerValue)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope outer =
                _unitOfWorkManager.Begin(SerializableOptions))
            {
                DbConnection outerConnection = outer.Db.Connection;
                DbTransaction outerTransaction = outer.Db.Transaction;
                _counterRepository.Insert(outerValue);

                using (global::NetUnitOfWorkManager.IUnitOfWorkScope inner =
                    _unitOfWorkManager.Begin(SerializableOptions))
                {
                    Expect(
                        ReferenceEquals(outerConnection, inner.Db.Connection),
                        "Nested Dapper scope must reuse the outer physical connection.");
                    Expect(
                        ReferenceEquals(outerTransaction, inner.Db.Transaction),
                        "Nested Dapper scope must reuse the outer physical transaction.");

                    _counterRepository.Insert(innerValue);
                    inner.Complete();
                }

                outer.Complete();
            }
        }

        public void CommitIndependentInsideSuppressionThenRollbackOuter(
            int outerValue,
            int independentValue)
        {
            using (global::NetUnitOfWorkManager.IUnitOfWorkScope outer =
                _unitOfWorkManager.Begin(SerializableOptions))
            {
                DbConnection outerConnection = outer.Db.Connection;
                DbTransaction outerTransaction = outer.Db.Transaction;

                using (_unitOfWorkManager.Suppress())
                {
                    Expect(!_unitOfWorkManager.HasCurrent, "Suppression must hide the outer Dapper root.");

                    using (global::NetUnitOfWorkManager.IUnitOfWorkScope independent =
                        _unitOfWorkManager.Begin(IndependentOptions))
                    {
                        Expect(
                            !ReferenceEquals(outerConnection, independent.Db.Connection),
                            "Suppress() + Begin() must create a different physical connection.");
                        Expect(
                            !ReferenceEquals(outerTransaction, independent.Db.Transaction),
                            "Suppress() + Begin() must create a different physical transaction.");

                        _counterRepository.Insert(independentValue);
                        independent.Complete();
                    }

                    Expect(
                        !_unitOfWorkManager.HasCurrent,
                        "After independent root finalization the suppression boundary must remain active.");
                }

                Expect(_unitOfWorkManager.HasCurrent, "Disposing suppression must restore the outer root.");
                Expect(
                    ReferenceEquals(outerConnection, _unitOfWorkManager.Current.Db.Connection),
                    "Suppression must restore the exact outer connection.");
                Expect(
                    ReferenceEquals(outerTransaction, _unitOfWorkManager.Current.Db.Transaction),
                    "Suppression must restore the exact outer transaction.");

                _counterRepository.Insert(outerValue);
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
