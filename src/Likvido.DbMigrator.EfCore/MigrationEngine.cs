using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Likvido.EfCore.MigrationsModel;
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

        // Which of the two models this run built, because it decides what the migration can see. A context
        // that opted in through UseMigrationsModel() carries every property, including the ones application
        // code currently ignores; one that did not carries the application model, and a migration generated
        // against that is missing every ignored column.
        //
        // ⚠️ Deliberately not a warning, and not a throw. A repository with no expand/contract ignores at all
        // is correct without the opt-in, so warning here would cry wolf on most of the estate — and throwing
        // would break every consumer on the next version bump. The real backstop needs no help from this
        // class: a repository that gates an Ignore and then forgets the opt-in gets a migrations model that
        // disagrees with its own snapshot, which is exactly what PendingModelChangesWarning is for, and
        // MigrateAsync below refuses to run. Nothing here suppresses that warning, on purpose.
        logger.LogInformation(
            context.IsMigrationsModel()
                ? "Using the complete migrations model (UseMigrationsModel)."
                : "Using the application model - this context did not opt in to UseMigrationsModel().");

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken)).ToList();
        pendingMigrations.Insert(0, "Pending migrations:");
        logger.LogInformation(string.Join($"{Environment.NewLine}", pendingMigrations.ToArray()));

        await context.Database.MigrateAsync(cancellationToken: cancellationToken);

        logger.LogInformation("Migrations successfully applied.");
    }
}
