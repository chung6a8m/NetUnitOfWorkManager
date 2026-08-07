using System;
using System.Collections.Generic;
using System.Data.Common;

namespace NetUnitOfWorkManager.Internal
{
    internal static class ResourceCleanup
    {
        internal static IReadOnlyList<Exception> Dispose(
            DbTransaction? transaction,
            DbConnection connection)
        {
            List<Exception>? failures = null;

            if (transaction != null)
            {
                try
                {
                    transaction.Dispose();
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            try
            {
                connection.Dispose();
            }
            catch (Exception exception)
            {
                AddFailure(ref failures, exception);
            }

            if (failures == null)
            {
                return Array.Empty<Exception>();
            }

            return failures;
        }

        private static void AddFailure(ref List<Exception>? failures, Exception exception)
        {
            if (failures == null)
            {
                failures = new List<Exception>();
            }

            failures.Add(exception);
        }
    }
}
