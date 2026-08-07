using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using NetUnitOfWorkManager;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunSmokeTest();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunSmokeTest()
    {
        string? connectionString = Environment.GetEnvironmentVariable("NETUOW_SQLSERVER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "NETUOW_SQLSERVER_CONNECTION_STRING is required for the P10 prerelease package smoke application.");
        }

        UnitOfWorkManager manager = new UnitOfWorkManager(
            () => new SqlConnection(connectionString));

        using (IUnitOfWorkScope outer = manager.Begin(
            new UnitOfWorkOptions(IsolationLevel.ReadCommitted)))
        {
            using (DbCommand create = outer.Db.CreateCommand())
            {
                create.CommandText = "CREATE TABLE #NetUowP10Smoke (Id int NOT NULL); INSERT INTO #NetUowP10Smoke (Id) VALUES (1);";
                create.ExecuteNonQuery();
            }

            using (IUnitOfWorkScope inner = manager.Begin(
                new UnitOfWorkOptions(IsolationLevel.ReadCommitted)))
            {
                using (DbCommand count = inner.Db.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM #NetUowP10Smoke;";
                    int rowCount = Convert.ToInt32(count.ExecuteScalar());
                    if (rowCount != 1)
                    {
                        throw new InvalidOperationException(
                            "Nested scope did not observe the root transaction state from the prerelease package.");
                    }
                }

                inner.Complete();
            }

            outer.Complete();
        }

        if (manager.HasCurrent)
        {
            throw new InvalidOperationException("Ambient Unit of Work was not cleared after commit.");
        }

        using (IUnitOfWorkScope followUp = manager.Begin())
        {
            using (DbCommand command = followUp.Db.CreateCommand())
            {
                command.CommandText = "SELECT 1;";
                int value = Convert.ToInt32(command.ExecuteScalar());
                if (value != 1)
                {
                    throw new InvalidOperationException("Follow-up provider command returned an unexpected result.");
                }
            }

            followUp.Complete();
        }

        Version? assemblyVersion = typeof(UnitOfWorkManager).Assembly.GetName().Version;
        Console.WriteLine(
            "P10 prerelease package smoke application passed on .NET Framework 4.7.2. Assembly version: {0}",
            assemblyVersion == null ? "unknown" : assemblyVersion.ToString());
    }
}
