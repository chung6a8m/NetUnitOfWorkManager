using System;

namespace NetUnitOfWorkManager.Internal
{
    internal sealed class UnitOfWorkScope : IUnitOfWorkScope
    {
        private readonly object _stateSync = new object();
        private readonly RootUnitOfWork _root;
        private readonly Action<RootUnitOfWork, bool> _settleScope;
        private UnitOfWorkScopeState _state;

        internal UnitOfWorkScope(
            RootUnitOfWork root,
            Action<RootUnitOfWork, bool> settleScope)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _settleScope = settleScope ?? throw new ArgumentNullException(nameof(settleScope));
            _state = UnitOfWorkScopeState.Active;
        }

        public UnitOfWorkDbSession Db
        {
            get
            {
                EnsureActive();
                return _root.Db;
            }
        }

        public bool IsRollbackRequested
        {
            get
            {
                EnsureActive();
                return _root.IsRollbackRequested;
            }
        }

        public void Complete()
        {
            Settle(UnitOfWorkScopeState.Completed, requestRollback: false);
        }

        public void Rollback()
        {
            Settle(UnitOfWorkScopeState.RolledBack, requestRollback: true);
        }

        public void Dispose()
        {
            lock (_stateSync)
            {
                if (_state != UnitOfWorkScopeState.Active)
                {
                    return;
                }

                _state = UnitOfWorkScopeState.Abandoned;
            }

            _settleScope(_root, true);
        }

        private void Settle(UnitOfWorkScopeState settledState, bool requestRollback)
        {
            lock (_stateSync)
            {
                EnsureActiveUnsafe();
                _state = settledState;
            }

            _settleScope(_root, requestRollback);
        }

        private void EnsureActive()
        {
            lock (_stateSync)
            {
                EnsureActiveUnsafe();
            }
        }

        private void EnsureActiveUnsafe()
        {
            if (_state != UnitOfWorkScopeState.Active)
            {
                throw new UnitOfWorkStateException(
                    $"The Unit of Work scope is already settled. Current state: {_state}.");
            }
        }
    }
}
