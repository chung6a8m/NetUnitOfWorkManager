using System;

namespace NetUnitOfWorkManager.Internal
{
    internal sealed class AmbientUnitOfWorkFrame
    {
        private AmbientUnitOfWorkFrame(
            RootUnitOfWork? root,
            long suppressionBoundaryId,
            AmbientUnitOfWorkFrame? parent)
        {
            Root = root;
            SuppressionBoundaryId = suppressionBoundaryId;
            Parent = parent;
        }

        internal RootUnitOfWork? Root { get; }

        internal long SuppressionBoundaryId { get; }

        internal AmbientUnitOfWorkFrame? Parent { get; }

        internal bool IsSuppressionBoundary => Root == null && SuppressionBoundaryId != 0;

        internal static AmbientUnitOfWorkFrame ForRoot(
            RootUnitOfWork root,
            AmbientUnitOfWorkFrame? parent)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            long suppressionBoundaryId = parent?.SuppressionBoundaryId ?? 0;
            return new AmbientUnitOfWorkFrame(root, suppressionBoundaryId, parent);
        }

        internal static AmbientUnitOfWorkFrame ForSuppression(
            long suppressionBoundaryId,
            AmbientUnitOfWorkFrame? parent)
        {
            if (suppressionBoundaryId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(suppressionBoundaryId));
            }

            return new AmbientUnitOfWorkFrame(null, suppressionBoundaryId, parent);
        }

        internal AmbientUnitOfWorkFrame WithParent(AmbientUnitOfWorkFrame? parent)
        {
            if (ReferenceEquals(Parent, parent))
            {
                return this;
            }

            return new AmbientUnitOfWorkFrame(Root, SuppressionBoundaryId, parent);
        }
    }
}
