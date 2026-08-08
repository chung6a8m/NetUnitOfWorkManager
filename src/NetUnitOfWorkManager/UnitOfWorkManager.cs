using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using NetUnitOfWorkManager.Internal;

namespace NetUnitOfWorkManager
{
    /// <summary>
    /// Coordinates ambient Unit of Work scopes that share one provider-native transaction.
    /// </summary>
    public sealed class UnitOfWorkManager : IUnitOfWorkManager
    {
        private readonly Func<DbConnection> _connectionFactory;
        private readonly AsyncLocal<AmbientUnitOfWorkFrame?> _ambient = new AsyncLocal<AmbientUnitOfWorkFrame?>();
        private long _nextSuppressionBoundaryId;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnitOfWorkManager"/> class.
        /// </summary>
        /// <param name="connectionFactory">Creates the provider-native connection owned by a new root Unit of Work.</param>
        public UnitOfWorkManager(Func<DbConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <inheritdoc/>
        public bool HasCurrent => _ambient.Value?.Root != null;

        /// <inheritdoc/>
        public IUnitOfWorkContext Current
        {
            get
            {
                RootUnitOfWork? root = _ambient.Value?.Root;

                if (root == null)
                {
                    throw new UnitOfWorkStateException("There is no current ambient Unit of Work.");
                }

                return root;
            }
        }

        /// <inheritdoc/>
        public IUnitOfWorkScope Begin(UnitOfWorkOptions? options = null)
        {
            AmbientUnitOfWorkFrame? currentFrame = _ambient.Value;
            RootUnitOfWork? root = currentFrame?.Root;

            if (root == null)
            {
                return BeginRoot(options, currentFrame);
            }

            ValidateNestedOptions(root, options);
            root.AcquireScope();
            return new UnitOfWorkScope(root, SettleScope);
        }

        /// <inheritdoc/>
        public IDisposable Suppress()
        {
            long boundaryId = Interlocked.Increment(ref _nextSuppressionBoundaryId);

            if (boundaryId <= 0)
            {
                throw new UnitOfWorkStateException("The ambient suppression boundary identity space has been exhausted.");
            }

            AmbientUnitOfWorkFrame boundary = AmbientUnitOfWorkFrame.ForSuppression(
                boundaryId,
                _ambient.Value);

            _ambient.Value = boundary;
            return new UnitOfWorkSuppression(this, boundary);
        }

        internal void RestoreSuppression(long boundaryId)
        {
            if (boundaryId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(boundaryId));
            }

            AmbientUnitOfWorkFrame? currentFrame = _ambient.Value;

            if (IsSuppressionBoundary(currentFrame, boundaryId))
            {
                _ambient.Value = currentFrame!.Parent;
                return;
            }

            if (!ContainsSuppressionBoundary(currentFrame, boundaryId))
            {
                return;
            }

            if (currentFrame?.Root != null)
            {
                throw new UnitOfWorkStateException(
                    "The suppression scope cannot be disposed while an independent Unit of Work started inside it is still active.");
            }

            throw new UnitOfWorkStateException(
                "Suppression scopes must be disposed in LIFO order.");
        }

        private IUnitOfWorkScope BeginRoot(
            UnitOfWorkOptions? options,
            AmbientUnitOfWorkFrame? parentFrame)
        {
            DbConnection? connection = _connectionFactory();

            if (connection == null)
            {
                throw new InvalidOperationException("The Unit of Work connection factory returned null.");
            }

            RootUnitOfWork root = RootUnitOfWork.Create(connection, options);
            root.AcquireScope();
            _ambient.Value = AmbientUnitOfWorkFrame.ForRoot(root, parentFrame);
            return new UnitOfWorkScope(root, SettleScope);
        }

        private static void ValidateNestedOptions(RootUnitOfWork root, UnitOfWorkOptions? options)
        {
            IsolationLevel? nestedIsolationLevel = options?.IsolationLevel;

            if (!nestedIsolationLevel.HasValue)
            {
                return;
            }

            if (root.RequestedIsolationLevel != nestedIsolationLevel)
            {
                throw new UnitOfWorkStateException(
                    $"Nested Unit of Work isolation level '{nestedIsolationLevel.Value}' does not match the root isolation level '{FormatIsolationLevel(root.RequestedIsolationLevel)}'.");
            }
        }

        private void SettleScope(RootUnitOfWork root, bool requestRollback)
        {
            bool shouldFinalize = root.ReleaseScope(requestRollback);

            if (!shouldFinalize)
            {
                return;
            }

            try
            {
                root.FinalizeTransaction();
            }
            finally
            {
                _ambient.Value = RemoveRootFromAmbientChain(_ambient.Value, root);
            }
        }

        private static AmbientUnitOfWorkFrame? RemoveRootFromAmbientChain(
            AmbientUnitOfWorkFrame? currentFrame,
            RootUnitOfWork root)
        {
            if (currentFrame == null)
            {
                return null;
            }

            Stack<AmbientUnitOfWorkFrame> framesAboveRoot = new Stack<AmbientUnitOfWorkFrame>();
            AmbientUnitOfWorkFrame? cursor = currentFrame;

            while (cursor != null && !ReferenceEquals(cursor.Root, root))
            {
                framesAboveRoot.Push(cursor);
                cursor = cursor.Parent;
            }

            if (cursor == null)
            {
                return currentFrame;
            }

            AmbientUnitOfWorkFrame? rebuilt = cursor.Parent;

            while (framesAboveRoot.Count > 0)
            {
                rebuilt = framesAboveRoot.Pop().WithParent(rebuilt);
            }

            return rebuilt;
        }

        private static bool IsSuppressionBoundary(
            AmbientUnitOfWorkFrame? frame,
            long boundaryId)
        {
            return frame != null &&
                frame.IsSuppressionBoundary &&
                frame.SuppressionBoundaryId == boundaryId;
        }

        private static bool ContainsSuppressionBoundary(
            AmbientUnitOfWorkFrame? currentFrame,
            long boundaryId)
        {
            AmbientUnitOfWorkFrame? cursor = currentFrame;

            while (cursor != null)
            {
                if (IsSuppressionBoundary(cursor, boundaryId))
                {
                    return true;
                }

                cursor = cursor.Parent;
            }

            return false;
        }

        private static string FormatIsolationLevel(IsolationLevel? isolationLevel)
        {
            return isolationLevel.HasValue ? isolationLevel.Value.ToString() : "provider default";
        }
    }
}
