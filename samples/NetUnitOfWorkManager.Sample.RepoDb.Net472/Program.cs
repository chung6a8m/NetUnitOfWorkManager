using System;
using Microsoft.Extensions.DependencyInjection;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Repositories;
using NetUnitOfWorkManager.Sample.RepoDb.Net472.Services;
using RepoDb;

namespace NetUnitOfWorkManager.Sample.RepoDb.Net472
{
    internal static class Program
    {
        private const string ConnectionStringEnvironmentVariable =
            "NETUOW_SQLSERVER_CONNECTION_STRING";

        private static int Main()
        {
            try
            {
                string? connectionString =
                    Environment.GetEnvironmentVariable("NETUOW_SQLSERVER_CONNECTION_STRING");

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.Error.WriteLine(
                        "Environment variable '" +
                        ConnectionStringEnvironmentVariable +
                        "' is not set.");
                    return 2;
                }

                GlobalConfiguration.Setup().UseSqlServer();

                var services = new ServiceCollection();
                services.AddSingleton(new SqlServerSampleDatabase(connectionString));
                services.AddSingleton<global::NetUnitOfWorkManager.IUnitOfWorkManager>(serviceProvider =>
                {
                    SqlServerSampleDatabase database =
                        serviceProvider.GetRequiredService<SqlServerSampleDatabase>();

                    return new global::NetUnitOfWorkManager.UnitOfWorkManager(
                        database.CreateConnection);
                });
                services.AddScoped<ICounterRepository, RepoDbCounterRepository>();
                services.AddScoped<NestedCounterService>();
                services.AddScoped<CounterApplicationService>();
                services.AddScoped<SampleRunner>();

                using (ServiceProvider serviceProvider = services.BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateOnBuild = true,
                        ValidateScopes = true
                    }))
                {
                    using (IServiceScope scope = serviceProvider.CreateScope())
                    {
                        scope.ServiceProvider
                            .GetRequiredService<SampleRunner>()
                            .Run();
                    }
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("RepoDb net472 sample failed:");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
