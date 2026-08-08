using System;

namespace NetUnitOfWorkManager.Internal
{
    internal sealed class UnitOfWorkSuppression : IDisposable
    {
        private readonly UnitOfWorkManager _manager;
        private readonly long _boundaryId;

        internal UnitOfWorkSuppression(
            UnitOfWorkManager manager,
            AmbientUnitOfWorkFrame boundary)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }

            _boundaryId = boundary.SuppressionBoundaryId;
        }

        public void Dispose()
        {
            _manager.RestoreSuppression(_boundaryId);
        }
    }
}
