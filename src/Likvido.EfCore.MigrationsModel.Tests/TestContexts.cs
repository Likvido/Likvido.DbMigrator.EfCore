using Likvido.EfCore.MigrationsModel;
using Microsoft.EntityFrameworkCore;

namespace Likvido.EfCore.MigrationsModel.Tests;

public class Thing
{
    public int Id { get; set; }
    public string? Existing { get; set; }

    /// <summary>A column mid-cycle: in the database, not yet used by application code.</summary>
    public string? NewColumn { get; set; }
}

/// <summary>
/// The gate written by hand, which <c>MigrationsOnly</c> now writes instead: the ignore gated on the migration
/// path, placed after the property's own mapping because above it would be silently undone. Kept because it is
/// the control for the two tests that show what the package's version does differently - it maps the property
/// on both paths, which is what makes EF report <c>MappedPropertyIgnoredWarning</c>.
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
/// An ungated ignore - the first of the three mistakes the hand-written gate allows, and unwritable through
/// <c>MigrationsOnly</c>. Present so the test suite states what is wrong with it rather than only describing it
/// in a comment.
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
/// The ordering footgun: the ignore sits above the property's own mapping, which silently undoes it. Compare
/// <see cref="MigrationsOnlyGateFirstContext"/>, where the same move is a no-op.
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

/// <summary>A table mid-cycle: in the database, not yet read by application code.</summary>
public class Gadget
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

/// <summary>A gated table that a context also declares a <c>DbSet</c> for, which is a release too early.</summary>
public class Widget
{
    public int Id { get; set; }
}

public class Customer
{
    public int Id { get; set; }
}

/// <summary>Carries a gated foreign key, which is the entangled case the single-property gate cannot hold.</summary>
public class Order
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>
/// The idiom the package provides, with every gate as the <em>last</em> statement of its block - the placement
/// the hand-written form requires. <see cref="MigrationsOnlyGateFirstContext"/> is the same model with every
/// gate first, and the pair is what proves placement no longer matters.
/// </summary>
public class MigrationsOnlyContext(DbContextOptions<MigrationsOnlyContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.Property(entity => entity.Existing).HasMaxLength(50);
            thing.MigrationsOnly(this, entity => entity.NewColumn, column => column.HasMaxLength(50));
        });

        modelBuilder.MigrationsOnly<Gadget>(this, gadget =>
        {
            gadget.ToTable("Gadgets");
            gadget.HasKey(entity => entity.Id);
            gadget.Property(entity => entity.Name).HasMaxLength(30);
        });
    }
}

/// <summary>
/// <see cref="MigrationsOnlyContext"/>'s model, with both gates moved to the front of their blocks. Under the
/// hand-written idiom this ordering silently loses the column - see <see cref="IgnoreBeforeMappingContext"/>.
/// </summary>
public class MigrationsOnlyGateFirstContext(DbContextOptions<MigrationsOnlyGateFirstContext> options)
    : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.MigrationsOnly<Gadget>(this, gadget =>
        {
            gadget.ToTable("Gadgets");
            gadget.HasKey(entity => entity.Id);
            gadget.Property(entity => entity.Name).HasMaxLength(30);
        });

        modelBuilder.Entity<Thing>(thing =>
        {
            thing.MigrationsOnly(this, entity => entity.NewColumn, column => column.HasMaxLength(50));
            thing.HasKey(entity => entity.Id);
            thing.Property(entity => entity.Existing).HasMaxLength(50);
        });
    }
}

/// <summary>
/// <see cref="MigrationsOnlyContext"/>'s model as the <em>next</em> release writes it: the gates replaced by
/// the ordinary EF calls. Its model is what the migrations model has to already equal, because that equality
/// is what makes release two need no migration.
/// </summary>
public class ReleaseTwoContext(DbContextOptions<ReleaseTwoContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.Property(entity => entity.Existing).HasMaxLength(50);
            thing.Property(entity => entity.NewColumn).HasMaxLength(50);
        });

        modelBuilder.Entity<Gadget>(gadget =>
        {
            gadget.ToTable("Gadgets");
            gadget.HasKey(entity => entity.Id);
            gadget.Property(entity => entity.Name).HasMaxLength(30);
        });
    }
}

/// <summary>Neither gate is given a configuration delegate, so EF's conventions map both.</summary>
public class MigrationsOnlyWithoutConfigureContext(DbContextOptions<MigrationsOnlyWithoutConfigureContext> options)
    : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.MigrationsOnly(this, entity => entity.NewColumn);
        });

        modelBuilder.MigrationsOnly<Gadget>(this);
    }
}

/// <summary>
/// A gated table with a <c>DbSet</c> declared for it, which is the shape the estate shipped by accident: the
/// model builds, and the first touch of the set throws.
/// </summary>
public class GatedTableWithDbSetContext(DbContextOptions<GatedTableWithDbSetContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.MigrationsOnly<Widget>(this, widget => widget.HasKey(entity => entity.Id));
}

/// <summary>
/// ⚠️ An index on the gated column, declared outside the gate and after it. The index's own call maps the
/// property again, which un-ignores it, so the application model gets both the column and the index. This
/// records EF's behaviour, and it is the reason the members overload exists.
/// </summary>
public class IndexAfterGateContext(DbContextOptions<IndexAfterGateContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.MigrationsOnly(this, entity => entity.NewColumn, column => column.HasMaxLength(50));
            thing.HasIndex(entity => entity.NewColumn);
        });
}

/// <summary>
/// The same index, inside the gate. The delegate also adds a shadow property, which is how the tests observe
/// whether it ran at all without relying on a counter - models are cached, so a counter would be sticky.
/// </summary>
public class IndexInsideGateContext(DbContextOptions<IndexInsideGateContext> options) : DbContext(options)
{
    public const string ConfigureRanMarker = "ConfigureRan";

    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.MigrationsOnly(
                this,
                [entity => entity.NewColumn],
                gated =>
                {
                    gated.Property(entity => entity.NewColumn).HasMaxLength(50);
                    gated.HasIndex(entity => entity.NewColumn);
                    gated.Property<string>(ConfigureRanMarker);
                });
        });
}

/// <summary>
/// ⚠️ A gated foreign key with the relationship configured outside the gate, above it. The relationship has
/// already mapped CustomerId explicitly, and an explicit mapping cannot be removed by ignoring it afterwards
/// while a foreign key is still using it - so the gate does nothing at all, and the application model keeps
/// the column, the foreign key and the navigation. EF reports it as MappedPropertyIgnoredWarning.
/// </summary>
public class ForeignKeyBeforeGateContext(DbContextOptions<ForeignKeyBeforeGateContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(customer => customer.HasKey(entity => entity.Id));

        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(entity => entity.Id);
            order.HasOne(entity => entity.Customer).WithMany().HasForeignKey(entity => entity.CustomerId);
            order.MigrationsOnly(this, entity => entity.CustomerId);
        });
    }
}

/// <summary>
/// The same foreign key done properly: the column, the navigation and the relationship all inside one gate.
/// </summary>
public class GatedForeignKeyContext(DbContextOptions<GatedForeignKeyContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(customer => customer.HasKey(entity => entity.Id));

        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(entity => entity.Id);
            order.MigrationsOnly(
                this,
                [entity => entity.CustomerId, entity => entity.Customer],
                gated => gated
                    .HasOne(entity => entity.Customer)
                    .WithMany()
                    .HasForeignKey(entity => entity.CustomerId));
        });
    }
}

/// <summary>
/// The gate written the wrong way round by hand, which is what <see cref="MigrationsModelDiagnostics"/>
/// exists to catch: the migrations model is the one missing the column.
/// </summary>
public class InvertedGateContext(DbContextOptions<InvertedGateContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasKey(entity => entity.Id);
            thing.Property(entity => entity.NewColumn).HasMaxLength(50);

            // ⚠️ Missing the `!`. Compiles, reads almost the same, leaves migrations blind to the column.
            if (this.IsMigrationsModel())
            {
                thing.Ignore(entity => entity.NewColumn);
            }
        });
}

/// <summary>Nothing gated at all - the control that stops the diagnostics tests being vacuous.</summary>
public class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Thing>(thing => thing.HasKey(entity => entity.Id));
}

/// <summary>
/// The entity-level twin of <see cref="InvertedGateContext"/>: the table is in the application model and
/// missing from migrations, so the next migration generated drops a table the application queries.
/// </summary>
public class InvertedTableGateContext(DbContextOptions<InvertedTableGateContext> options) : DbContext(options)
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gadget>(gadget => gadget.HasKey(entity => entity.Id));

        // ⚠️ Missing the `!`, on a whole table this time.
        if (this.IsMigrationsModel())
        {
            modelBuilder.Ignore<Gadget>();
        }
    }
}
