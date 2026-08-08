using System;
using Microsoft.Extensions.DependencyInjection;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Infrastructure;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Repositories;
using NetUnitOfWorkManager.Sample.Dapper.Net472.Services;

namespace NetUnitOfWorkManager.Sample.Dapper.Net472
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
                    Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.Error.WriteLine(
                        "Environment variable '" +
                        ConnectionStringEnvironmentVariable +
                        "' is not set.");
                    return 2;
                }

                var services = new ServiceCollection();
                services.AddSingleton(new SampleDatabase(connectionString));
                services.AddSingleton<global::NetUnitOfWorkManager.IUnitOfWorkManager>(serviceProvider =>
                {
                    SampleDatabase database = serviceProvider.GetRequiredService<SampleDatabase>();
                    return new global::NetUnitOfWorkManager.UnitOfWorkManager(database.CreateConnection);
                });
                services.AddScoped<ICounterRepository, DapperCounterRepository>();
                services.AddScoped<CounterService>();
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
                        scope.ServiceProvider.GetRequiredService<SampleRunner>().Run();
                    }
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Dapper net472 sample failed:");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
