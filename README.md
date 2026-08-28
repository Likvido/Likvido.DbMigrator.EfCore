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
`UseMigrationsModel()` in the design-time factory and `MigrationsOnly(...)` on each member that is a release
ahead of the code.

Both live in **`Likvido.EfCore.MigrationsModel`**, which depends on nothing but EF Core. Reference it from
the project that holds the `DbContext` — the gate has to be written there, and that project cannot take
`Likvido.DbMigrator.EfCore`'s dependency on `Likvido.Robot`. A migrator project gets it transitively.

```
Likvido.Whatever.Database   -> Likvido.EfCore.MigrationsModel     (the gate)
Likvido.Whatever.DbMigrator -> Likvido.DbMigrator.EfCore          (which depends on it)
```

```csharp
// The design-time factory - used by the migrator and by `dotnet ef`, and by nothing else.
var builder = new DbContextOptionsBuilder<ThingContext>()
    .UseSqlServer(connectionString)
    .UseMigrationsModel();

// The context - one class, two models. One call per member that is a release ahead of the code.
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // A new table.
    modelBuilder.MigrationsOnly<Gadget>(this, gadget =>
    {
        gadget.ToTable("Gadgets");
        gadget.HasKey(x => x.Id);
    });

    modelBuilder.Entity<Thing>(thing =>
    {
        // A new column.
        thing.MigrationsOnly(this, x => x.NewColumn, column => column.HasMaxLength(50));

        // A new column that also needs an index, a key or a relationship - anything the property builder
        // cannot reach. Everything about the gated members goes inside the one call.
        thing.MigrationsOnly(
            this,
            [x => x.CustomerId, x => x.Customer],
            gated =>
            {
                gated.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
                gated.HasIndex(x => x.CustomerId);
            });
    });
}
```

The next release replaces each call with the ordinary EF one and needs **no migration**, because the
migrations model already described the member:

```diff
-modelBuilder.MigrationsOnly<Gadget>(this, gadget =>
+modelBuilder.Entity<Gadget>(gadget =>

-thing.MigrationsOnly(this, x => x.NewColumn, column => column.HasMaxLength(50));
+thing.Property(x => x.NewColumn).HasMaxLength(50);
```

A removal runs the same edit backwards: turn the ordinary call into a `MigrationsOnly` one in the release that
stops using the member, then delete it and generate the drop in the release after. So everything still
outstanding in a repository is one `grep MigrationsOnly` — nothing here enforces that the second release
happens, but it is at least countable.

Two things belong in the release that *uses* a gated table, not the one that ships it: its `DbSet` property
and any navigation pointing at it. A `DbSet` for a gated type throws on first touch; a navigation to it is
worse, because EF discovers it and quietly puts the whole table back into the application model.

### The order of the steps does not matter

Write the gate and generate the migration in whichever order suits you. The migrations model skips the gate,
so `dotnet ef migrations add` sees the property either way and produces the same migration. Verified both
ways round against a real database.

⚠️ **That holds only because the ignore is gated.** An *ungated* ignore hides the property from the
migrations model too, so generating a migration after adding one produces an **empty** migration — the change
you meant to make silently does not exist.

### Do not write the gate by hand

The condition `MigrationsOnly` writes for you used to be written at each call site, and there were three ways
to write it that compile, read correctly in review, and lose the column: leaving the `Ignore` ungated,
inverting the condition, and putting the `Ignore` above the mapping it was meant to hide, where the later
mapping silently undoes it. None of the three is reachable through `MigrationsOnly`. `IsMigrationsModel()`
stays public for the one thing the gate cannot express — configuration that has to *differ* between the two
models rather than be absent from one of them, a global query filter over a gated column being the real case,
since a filter simply moved inside a gate would leave the application with no filter at all.

### Two things that will still cost you a column

- **Every mention of a gated member has to be inside its gate.** `MigrationsOnly` owns the order of the calls
  it makes, which is why placement in `OnModelCreating` no longer matters; it cannot own calls it never sees.
  An index, key, relationship or `HasData` naming a gated member from outside its gate either does nothing at
  all (if it is above the gate) or puts the member straight back into the application model (if it is below).
  The overload taking a list of members exists so that all of it fits in one call.
- **Do not suppress `PendingModelChangesWarning`.** A gated member can no longer put the model and the
  snapshot out of step, so it will not fire for that. When it does fire it means a migration is genuinely
  missing, and the answer is to generate it rather than to switch the warning off. It is also the backstop
  if you forget `UseMigrationsModel()`: the migrations model then disagrees with its own snapshot and the
  migrator refuses to run, instead of quietly applying the wrong thing.

### Two warnings worth turning into errors

A model built through `MigrationsOnly` never maps a member before ignoring it, so these two never fire — which
leaves them free to mean something. Both fire exactly when a gated member was already mapped by configuration
outside its gate, and both also fire on a gate written by hand:

```csharp
options.ConfigureWarnings(warnings => warnings.Throw(
    CoreEventId.MappedPropertyIgnoredWarning,
    CoreEventId.MappedEntityTypeIgnoredWarning));
```

Verified on EF Core 10.0.11. It does not catch configuration placed *after* a gate — EF sees a mapping and no
ignore, so it has no opinion — and what catches that is the first query against a column the migration has not
created yet.

### The invariant to assert in your own test suite

`MigrationsModelDiagnostics` compares the two models so each repository does not have to:

```csharp
using var migrations = new ThingDesignTimeDbContextFactory().CreateDbContext([]);
using var application = new ThingContext(applicationOptions);

MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
```

A superset rather than an equality, deliberately: it holds whether or not anything is gated, so adopting or
removing a gate never means editing the test. Pair it with `context.Database.HasPendingModelChanges()` on the
migrations context, which is the half that catches a member missing from *both* models.

`UseMigrationsModel()` belongs in the design-time factory only. Calling it during application startup gives
running code columns the database may not have yet, which is the failure the whole pattern exists to avoid.

A worked cycle for each kind of change — adding a column, adding a table, adding an index, removing either,
renaming, making a nullable column required — is in NewWiki at
`developer-workflow/environment-and-setup/DATABASE_MIGRATIONS.md`. Worth knowing before reaching for the
list: an index on columns that already exist needs no gate and ships in one release, because application code
never names it, so neither deploy order can break. An index on a *gated* column is a different thing, and it
goes inside that column's gate.

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
