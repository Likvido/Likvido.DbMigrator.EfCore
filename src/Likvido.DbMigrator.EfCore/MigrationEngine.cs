using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Likvido.Robot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging;

namespace Likvido.DbMigrator.EfCore;

[UsedImplicitly]
public class MigrationEngine<TContext, TContextFactory>(
    ILogger<MigrationEngine<TContext, TContextFactory>> logger,
    IDesignTimeDbContextFactory<TContext> designTimeDbContextFactory) : ILikvidoRobotEngine where TContext : DbContext
{
    public async Task Run(CancellationToken cancellationToken)
    {
        var context = designTimeDbContextFactory.CreateDbContext([]);
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken)).ToList();
        pendingMigrations.Insert(0, "Pending migrations:");
        logger.LogInformation(string.Join($"{Environment.NewLine}", pendingMigrations.ToArray()));

        await context.Database.MigrateAsync(cancellationToken: cancellationToken);
    }
}
