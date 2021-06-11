using System;
using System.Linq;
using System.Threading.Tasks;
using Likvido.EfCore.SqlServerContextFactory;
using Likvido.Extensions.Logging;
using Likvido.Robots;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Likvido.DbMigrator.EfCore
{
    public class MigrationsRunner<TContext, TContextFactory>
        where TContextFactory : IDesignTimeDbContextFactory<TContext>
        where TContext: DbContext
    {
        private ILogger<MigrationsRunner<TContext, TContextFactory>>? _logger;
        private readonly string _role;

        public MigrationsRunner(string role)
        {
            _role = role;
        }

        public async Task Run(string operationName = "db-migration")
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                await DoRun(operationName);
            }
            catch (Exception e)
            {
                Log.Fatal("Set up failed", e);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        protected virtual async Task DoRun(string operationName)
        {
            await new Robot(_role)
                .BuildOperation(operationName)
                .SetFunc(Execute)
                .SetReportError((message, ex) =>
                {
                    if (_logger != null)
                    {
                        _logger.LogCritical(ex, message);
                    }
                    else
                    {
                        Log.Fatal(message, ex);
                    }
                })
                .SetConfigureServices(Configure)
                .SetOnServiceProviderBuild(sp =>
                    _logger = sp.GetRequiredService<ILogger<MigrationsRunner<TContext, TContextFactory>>>())
                .SetPostExecute(LoggerShutdown)
                .Run();
        }

        protected virtual Task LoggerShutdown()
        {
            Log.CloseAndFlush();
            return Task.CompletedTask;
        }

        protected virtual void Configure(IConfiguration configuration, IServiceCollection services)
        {
            services.AddSingleton<ContextFactory<TContext>>();
            services.UseSerilog((serviceProvider, loggerConfiguration) =>
            {
                var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
                loggerConfiguration
                    .ReadFrom.Configuration(configuration)
                    .Enrich.WithMachineName()
                    .WriteTo.ApplicationInsights(telemetryClient, TelemetryConverter.Traces)
                    .WriteTo.Console()
                    .Enrich.FromLogContext();
            });
        }

        protected virtual async Task Execute(IServiceProvider services)
        {
            var contextFactory = ActivatorUtilities.CreateInstance<TContextFactory>(services);
            var context = contextFactory.CreateDbContext(new string[0]);
            var db = context.Database;

            var pendingMigrations = (await db.GetPendingMigrationsAsync()).ToList();
            pendingMigrations.Insert(0, "Pending migrations:");
            _logger.LogInformation(string.Join($"{Environment.NewLine}", pendingMigrations.ToArray()));

            await db.MigrateAsync();
        }
    }
}
