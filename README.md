# Likvido.DbMigrator.EfCore [![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Likvido/Likvido.DbMigrator.EfCore/nuget.yml)](https://github.com/Likvido/Likvido.DbMigrator.EfCore/actions/workflows/nuget.yml) [![Nuget](https://img.shields.io/nuget/v/Likvido.DbMigrator.EfCore)](https://www.nuget.org/packages/Likvido.DbMigrator.EfCore/)
Should be used to simplify the process of applying migrations
# Usage
## Run as a job
```
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private readonly ContextFactory<AppDbContext> _contextFactory;

    public ApplicationDbContextFactory(ContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public ApplicationDbContextFactory()
    {
        _contextFactory = new ContextFactory<AppDbContext>("../ProjectWithAppSettings");
    }

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        return _contextFactory.CreateDbContext("ProjectWithMigrations");
    }
}

static async Task Main(string[] args)
{
    await new MigrationsRunner<ApplicationDbContext, ApplicationDbContextFactory>("role-for-telemetry").Run();
}
```
## Run migrations from Package Manager Console
Since we often have more than one context we should be specific. Default project should be a project that contains the specified context.
```
Update-Database -Verbose -Context AppDbContext 
```
## Run migrations from console
Should be executed in a project folder where the specified context defined
```
dotnet ef database update --context AppDbContext
```
