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
## Expand/contract: shipping a schema change ahead of the code that uses it

A deploy gives no guarantee about whether the migration or the new application code lands first, and
ordering it does not help — running the migration first only guarantees that the *old* code meets the *new*
schema for the length of the rollout. The fix is to make every schema change work in both directions:

1. Ship the schema change with the application code **ignoring** the new table or column.
2. In the **next** release, remove the ignore and start using it.
3. Removals run the cycle backwards: stop using it, deploy, then drop it.

EF6 does this with two contexts, the base one being the migrations model. With one EF Core context, use
`UseMigrationsModel()` in the design-time factory and gate the ignore on `IsMigrationsModel()`.

Both live in **`Likvido.EfCore.MigrationsModel`**, which depends on nothing but EF Core. Reference it from
the project that holds the `DbContext` — the gate has to be written there, and that project cannot take
`Likvido.DbMigrator.EfCore`'s dependency on `Likvido.Robot`. A migrator project gets it transitively.

```
Likvido.Whatever.Database   -> Likvido.EfCore.MigrationsModel     (the gate)
Likvido.Whatever.DbMigrator -> Likvido.DbMigrator.EfCore          (which depends on it)
```

```csharp
// The context - one class, two models.
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Thing>(thing =>
    {
        thing.Property(x => x.NewColumn).HasMaxLength(50);

        // The application does not see the column yet. Migrations do.
        if (!this.IsMigrationsModel())
        {
            thing.Ignore(x => x.NewColumn);
        }
    });
}

// The design-time factory - used by the migrator and by `dotnet ef`, and by nothing else.
var builder = new DbContextOptionsBuilder<ThingContext>()
    .UseSqlServer(connectionString)
    .UseMigrationsModel();
```

### The order of the steps does not matter

Write the gate and generate the migration in whichever order suits you. The migrations model skips the gate,
so `dotnet ef migrations add` sees the property either way and produces the same migration. Verified both
ways round against a real database.

⚠️ **That holds only because the ignore is gated.** An *ungated* ignore hides the property from the
migrations model too, so generating a migration after adding one produces an **empty** migration — the change
you meant to make silently does not exist.

### Three things that will cost you a column

- **Do not put the `Ignore` above the property's own `Property(...)` call.** The later mapping puts the
  property back and nothing warns you. Gate it *after* the mapping. The same applies to `Ignore<T>()` for a
  whole entity: it goes after the `Entity<T>()` block, not before it.
- **Do not leave the `Ignore` ungated.** An unconditional `Ignore` takes the column out of the migrations
  model too, so the next migration anyone generates — for something unrelated — diffs a model without the
  column against a snapshot with it and emits a `DropColumn`.
- **Do not suppress `PendingModelChangesWarning`.** A gated ignore can no longer put the model and the
  snapshot out of step, so it will not fire for that. When it does fire it means a migration is genuinely
  missing, and the answer is to generate it rather than to switch the warning off. It is also the backstop
  if you forget `UseMigrationsModel()`: the migrations model then disagrees with its own snapshot and the
  migrator refuses to run, instead of quietly applying the wrong thing.

`UseMigrationsModel()` belongs in the design-time factory only. Calling it during application startup gives
running code columns the database may not have yet, which is the failure the whole pattern exists to avoid.

A worked cycle for each kind of change — adding a column, adding a table, adding an index, removing either,
renaming, making a nullable column required — is in NewWiki at
`developer-workflow/environment-and-setup/DATABASE_MIGRATIONS.md`. Worth knowing before reaching for the
list: an index needs no gate and ships in one release, because application code never names it, so neither
deploy order can break.

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
