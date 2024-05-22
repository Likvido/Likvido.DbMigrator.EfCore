using System.Threading.Tasks;
using JetBrains.Annotations;
using Likvido.Robot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Likvido.DbMigrator.EfCore;

[PublicAPI]
public class MigrationsRunner<TContext, TContextFactory>(string role)
    where TContextFactory : class, IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    public async Task Run(string operationName = "db-migration") =>
        await RobotOperation.Run<MigrationEngine<TContext, TContextFactory>>(
            role,
            operationName,
            (_, services) =>
            {
                services.AddSingleton<IDesignTimeDbContextFactory<TContext>, TContextFactory>();
            });
}
