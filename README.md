# Likvido.DbMigrator.EfCore [![GitHub Workflow Status](https://img.shields.io/github/workflow/status/likvido/Likvido.DbMigrator.EfCore/Publish%20to%20nuget)](https://github.com/Likvido/Likvido.DbMigrator.EfCore/actions?query=workflow%3A%22Publish+to+nuget%22) [![Nuget](https://img.shields.io/nuget/v/Likvido.DbMigrator.EfCore)](https://www.nuget.org/packages/Likvido.DbMigrator.EfCore/)
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
