using System;
using System.Collections.Generic;
using System.Linq;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Models;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Services;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472
{
    public sealed class SampleRunner
    {
        private readonly SqlServerSampleDatabase _database;
        private readonly CounterApplicationService _counterService;

        public SampleRunner(
            SqlServerSampleDatabase database,
            CounterApplicationService counterService)
        {
            _database = database;
            _counterService = counterService;
        }

        public void Run()
        {
            _database.EnsureCreated();
            _database.Reset();

            Console.WriteLine("NetUnitOfWorkManager + RepoDb + Microsoft.Extensions.DependencyInjection");
            Console.WriteLine("Target framework: " + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName);
            Console.WriteLine();

            Console.WriteLine("Scenario 1: nested scopes complete -> commit");
            _counterService.CommitPair(10, 20);
            IReadOnlyList<CounterItem> committedItems = _counterService.List();
            PrintItems(committedItems);
            ExpectValues(committedItems, 10, 20);
            Console.WriteLine("PASS: both rows were committed.");
            Console.WriteLine();

            Console.WriteLine("Scenario 2: inner scope is abandoned -> rollback-only");
            _counterService.RollbackPair(30, 40);
            IReadOnlyList<CounterItem> afterRollbackItems = _counterService.List();
            PrintItems(afterRollbackItems);
            ExpectValues(afterRollbackItems, 10, 20);
            Console.WriteLine("PASS: rollback removed both rows from the second pair.");
            Console.WriteLine();

            _database.Reset();
            Console.WriteLine("Scenario 3: suppression creates an independent root transaction");
            _counterService.CommitIndependentInsideSuppressionThenRollbackOuter(50, 60);
            IReadOnlyList<CounterItem> afterSuppression = _counterService.List();
            PrintItems(afterSuppression);
            ExpectValues(afterSuppression, 60);
            Console.WriteLine("PASS: independent RepoDb commit survived the outer rollback.");
            Console.WriteLine();

            Console.WriteLine("All RepoDb net472 sample scenarios passed.");
        }

        private static void PrintItems(IReadOnlyList<CounterItem> items)
        {
            if (items.Count == 0)
            {
                Console.WriteLine("  <empty>");
                return;
            }

            foreach (CounterItem item in items)
            {
                Console.WriteLine("  Id=" + item.Id + ", Value=" + item.Value);
            }
        }

        private static void ExpectValues(
            IReadOnlyList<CounterItem> items,
            params int[] expectedValues)
        {
            int[] actualValues = items.Select(item => item.Value).ToArray();

            if (!actualValues.SequenceEqual(expectedValues))
            {
                throw new InvalidOperationException(
                    "Unexpected counter values. Expected: [" +
                    string.Join(", ", expectedValues) +
                    "]; actual: [" +
                    string.Join(", ", actualValues) +
                    "].");
            }
        }
    }
}
