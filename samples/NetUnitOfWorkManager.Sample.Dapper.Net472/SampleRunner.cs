using System;
using System.Collections.Generic;
using System.Linq;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Models;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Services;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472
{
    public sealed class SampleRunner
    {
        private readonly SampleDatabase _database;
        private readonly CounterService _counterService;

        public SampleRunner(SampleDatabase database, CounterService counterService)
        {
            _database = database;
            _counterService = counterService;
        }

        public void Run()
        {
            _database.EnsureCreated();

            Console.WriteLine("NetUnitOfWorkManager + Dapper + Microsoft.Extensions.DependencyInjection");
            Console.WriteLine("Target framework: " + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName);
            Console.WriteLine();

            _database.Reset();
            Console.WriteLine("Scenario 1: single UoW commit");
            _counterService.CommitSingle(10);
            ExpectValues(_counterService.List(), 10);
            Console.WriteLine("PASS: committed row is visible.");
            Console.WriteLine();

            _database.Reset();
            Console.WriteLine("Scenario 2: explicit rollback");
            _counterService.RollbackSingle(20);
            ExpectValues(_counterService.List());
            Console.WriteLine("PASS: rolled-back row is not visible.");
            Console.WriteLine();

            _database.Reset();
            Console.WriteLine("Scenario 3: nested service scopes reuse one physical transaction");
            _counterService.CommitNestedPair(30, 40);
            ExpectValues(_counterService.List(), 30, 40);
            Console.WriteLine("PASS: nested rows committed through one physical root transaction.");
            Console.WriteLine();

            _database.Reset();
            Console.WriteLine("Scenario 4: suppression creates an independent root transaction");
            _counterService.CommitIndependentInsideSuppressionThenRollbackOuter(50, 60);
            IReadOnlyList<CounterItem> afterSuppression = _counterService.List();
            PrintItems(afterSuppression);
            ExpectValues(afterSuppression, 60);
            Console.WriteLine("PASS: independent inner commit survived the outer rollback.");
            Console.WriteLine();

            Console.WriteLine("All Dapper net472 sample scenarios passed.");
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
