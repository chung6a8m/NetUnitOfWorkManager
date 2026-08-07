using System;
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
        private readonly AsyncLocal<RootUnitOfWork?> _current = new AsyncLocal<RootUnitOfWork?>();

        /// <summary>
        /// Initializes a new instance of the <see cref="UnitOfWorkManager"/> class.
        /// </summary>
        /// <param name="connectionFactory">Creates the provider-native connection owned by a new root Unit of Work.</param>
        public UnitOfWorkManager(Func<DbConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <inheritdoc/>
        public bool HasCurrent => _current.Value != null;

        /// <inheritdoc/>
        public IUnitOfWorkContext Current
        {
            get
            {
                RootUnitOfWork? root = _current.Value;

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
            RootUnitOfWork? root = _current.Value;

            if (root == null)
            {
                return BeginRoot(options);
            }

            ValidateNestedOptions(root, options);
            root.AcquireScope();
            return new UnitOfWorkScope(root, SettleScope);
        }

        private IUnitOfWorkScope BeginRoot(UnitOfWorkOptions? options)
        {
            DbConnection? connection = _connectionFactory();

            if (connection == null)
            {
                throw new InvalidOperationException("The Unit of Work connection factory returned null.");
            }

            RootUnitOfWork root = RootUnitOfWork.Create(connection, options);
            root.AcquireScope();
            _current.Value = root;
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
                if (ReferenceEquals(_current.Value, root))
                {
                    _current.Value = null;
                }
            }
        }

        private static string FormatIsolationLevel(IsolationLevel? isolationLevel)
        {
            return isolationLevel.HasValue ? isolationLevel.Value.ToString() : "provider default";
        }
    }
}
