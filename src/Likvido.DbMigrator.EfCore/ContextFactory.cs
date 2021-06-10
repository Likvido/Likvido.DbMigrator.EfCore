using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Likvido.Payables.Services.Data
{
    public class ContextFactory<TContext> where TContext : DbContext
    {
        private readonly string? _settingsPath;
        private readonly string? _environmentName;
        private IConfiguration? _configuration;
        private readonly ILoggerFactory? _loggerFactory;

        public ContextFactory(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }

        public ContextFactory(string settingsPath) :
            this(settingsPath, Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
        {
        }

        public ContextFactory(string settingsPath, string environmentName)
        {
            if (string.IsNullOrEmpty(environmentName!))
            {
                throw new ArgumentException($"{nameof(environmentName)} is empty");
            }

            _settingsPath = settingsPath;
            _environmentName = environmentName;
        }

        public TContext CreateDbContext(string migrationsAssembly, string connectionStringName = "DefaultConnection")
        {
            var optionsBuilder = new DbContextOptionsBuilder<TContext>();
            if (_loggerFactory != null)
            {
                optionsBuilder.UseLoggerFactory(_loggerFactory);
            }
            var options = optionsBuilder
                .UseSqlServer(
                    GetConfiguration(_settingsPath).GetConnectionString(connectionStringName),
                    opts => opts.CommandTimeout((int)TimeSpan.FromHours(1).TotalSeconds)
                .EnableRetryOnFailure()
                .MigrationsAssembly(migrationsAssembly))
                .Options;

            return (TContext)Activator.CreateInstance(typeof(TContext), options);
        }

        public IConfiguration GetConfiguration(string? settingsPath)
        {
            if (_configuration == null)
            {
                _configuration = CreateConfiguration(settingsPath);
            }

            return _configuration;
        }

        public IConfiguration CreateConfiguration(string? settingsPath)
        {
            string basePath = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(settingsPath))
            {
                basePath = Path.Combine(basePath, settingsPath);
            }

            return new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{_environmentName}.json", optional: true)
                .AddJsonFile($"appsettings.local.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
        }
    }
}
