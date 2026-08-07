using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;

namespace NetUnitOfWorkManager.Internal
{
    internal sealed class RootUnitOfWork
    {
        private readonly object _lifecycleSync = new object();
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private readonly UnitOfWorkDbSession _db;
        private UnitOfWorkLifecycleState _state;
        private bool _rollbackRequested;

        private RootUnitOfWork(DbConnection connection, DbTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
            _db = new UnitOfWorkDbSession(connection, transaction);
            _state = UnitOfWorkLifecycleState.Active;
        }

        internal UnitOfWorkLifecycleState State
        {
            get
            {
                lock (_lifecycleSync)
                {
                    return _state;
                }
            }
        }

        internal UnitOfWorkDbSession Db
        {
            get
            {
                lock (_lifecycleSync)
                {
                    EnsureActive();
                    return _db;
                }
            }
        }

        internal bool IsRollbackRequested
        {
            get
            {
                lock (_lifecycleSync)
                {
                    return _rollbackRequested;
                }
            }
        }

        internal static RootUnitOfWork Create(DbConnection connection, UnitOfWorkOptions? options)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            DbTransaction? transaction = null;

            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                }

                IsolationLevel? isolationLevel = options?.IsolationLevel;
                transaction = isolationLevel.HasValue
                    ? connection.BeginTransaction(isolationLevel.Value)
                    : connection.BeginTransaction();

                if (transaction == null)
                {
                    throw new InvalidOperationException("The database provider returned a null transaction.");
                }

                return new RootUnitOfWork(connection, transaction);
            }
            catch (Exception primaryFailure)
            {
                IReadOnlyList<Exception> cleanupFailures = ResourceCleanup.Dispose(transaction, connection);
                ThrowFailures(primaryFailure, cleanupFailures, "Unit of Work initialization failed and resource cleanup also encountered errors.");
                throw;
            }
        }

        internal void RequestRollback()
        {
            lock (_lifecycleSync)
            {
                EnsureActive();
                _rollbackRequested = true;
            }
        }

        internal void FinalizeTransaction()
        {
            bool rollbackRequested;

            lock (_lifecycleSync)
            {
                EnsureActive();
                _state = UnitOfWorkLifecycleState.Finalizing;
                rollbackRequested = _rollbackRequested;
            }

            Exception? primaryFailure = null;

            try
            {
                if (rollbackRequested)
                {
                    _transaction.Rollback();
                }
                else
                {
                    _transaction.Commit();
                }
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
            }

            IReadOnlyList<Exception> cleanupFailures = ResourceCleanup.Dispose(_transaction, _connection);
            bool faulted = primaryFailure != null || cleanupFailures.Count != 0;

            lock (_lifecycleSync)
            {
                _state = faulted
                    ? UnitOfWorkLifecycleState.Faulted
                    : UnitOfWorkLifecycleState.Disposed;
            }

            ThrowFailures(
                primaryFailure,
                cleanupFailures,
                "Unit of Work finalization failed and resource cleanup also encountered errors.");
        }

        private void EnsureActive()
        {
            if (_state != UnitOfWorkLifecycleState.Active)
            {
                throw new UnitOfWorkStateException(
                    $"The root Unit of Work is not active. Current state: {_state}.");
            }
        }

        private static void ThrowFailures(
            Exception? primaryFailure,
            IReadOnlyList<Exception> cleanupFailures,
            string aggregateMessage)
        {
            if (primaryFailure == null && cleanupFailures.Count == 0)
            {
                return;
            }

            if (primaryFailure != null && cleanupFailures.Count == 0)
            {
                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
                return;
            }

            if (primaryFailure == null && cleanupFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                return;
            }

            List<Exception> failures = new List<Exception>(cleanupFailures.Count + 1);

            if (primaryFailure != null)
            {
                failures.Add(primaryFailure);
            }

            for (int index = 0; index < cleanupFailures.Count; index++)
            {
                failures.Add(cleanupFailures[index]);
            }

            throw new AggregateException(aggregateMessage, failures);
        }
    }
}
