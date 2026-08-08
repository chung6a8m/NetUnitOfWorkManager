using System;

namespace NetUnitOfWorkManager.Internal
{
    internal sealed class UnitOfWorkSuppression : IDisposable
    {
        private readonly object _sync = new object();
        private readonly UnitOfWorkManager _manager;
        private readonly AmbientUnitOfWorkFrame _boundary;
        private bool _disposed;

        internal UnitOfWorkSuppression(
            UnitOfWorkManager manager,
            AmbientUnitOfWorkFrame boundary)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _manager.RestoreSuppression(_boundary);
                _disposed = true;
            }
        }
    }
}
