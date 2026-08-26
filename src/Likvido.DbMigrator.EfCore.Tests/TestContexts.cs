using Likvido.DbMigrator.EfCore;
using Microsoft.EntityFrameworkCore;

namespace Likvido.DbMigrator.EfCore.Tests;

public class Thing
{
    public int Id { get; set; }
    public string? Existing { get; set; }

    /// <summary>A column mid-cycle: in the database, not yet used by application code.</summary>
    public string? NewColumn { get; set; }
}

/// <summary>
/// The idiom this package exists to support: one context, the expand/contract ignore gated on the
/// migration path, and the gate placed after the property's own mapping.
/// </summary>
public class GatedContext(DbContextOptions<GatedContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(x => x.Id);
            thing.Property(x => x.Existing).HasMaxLength(50);
            thing.Property(x => x.NewColumn).HasMaxLength(50);

            // ⚠️ After the mapping above, never before it - see IgnoreBeforeMappingContext.
            if (!this.IsMigrationsModel())
            {
                thing.Ignore(x => x.NewColumn);
            }
        });
    }
}

/// <summary>
/// What the estate does today: an ungated ignore. Present so the test suite states what is wrong with it
/// rather than only describing it in a comment.
/// </summary>
public class UngatedContext(DbContextOptions<UngatedContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(x => x.Id);
            thing.Property(x => x.NewColumn).HasMaxLength(50);
            thing.Ignore(x => x.NewColumn);
        });
    }
}

/// <summary>
/// The ordering footgun: the ignore sits above the property's own mapping, which silently undoes it.
/// </summary>
public class IgnoreBeforeMappingContext(DbContextOptions<IgnoreBeforeMappingContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(x => x.Id);
            thing.Ignore(x => x.NewColumn);
            thing.Property(x => x.NewColumn).HasMaxLength(50);
        });
    }
}
