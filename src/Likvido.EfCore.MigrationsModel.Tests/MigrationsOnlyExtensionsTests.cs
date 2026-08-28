using Likvido.EfCore.MigrationsModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shouldly;
using Xunit;

namespace Likvido.EfCore.MigrationsModel.Tests;

/// <summary>
/// The property under test throughout: a <c>MigrationsOnly</c> call maps its member in the migrations model
/// and hides it from the application model, and does so without the caller having to place it anywhere in
/// particular. The three mistakes the hand-written gate allows are unwritable here, and the tests near the end
/// of this file record the two entangled cases that are still possible, because those are what the members
/// overload exists for.
/// </summary>
public class MigrationsOnlyExtensionsTests
{
    private const string ConnectionString = "Server=localhost;Database=tests;Trusted_Connection=True;";

    private static TContext Build<TContext>(bool migrationsModel, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>().UseSqlServer(ConnectionString);

        if (migrationsModel)
        {
            builder.UseMigrationsModel();
        }

        return create(builder.Options);
    }

    private static bool HasEntityType(DbContext context, Type clrType) =>
        context.Model.FindEntityType(clrType) is not null;

    private static bool HasProperty(DbContext context, Type clrType, string name) =>
        context.Model.FindEntityType(clrType)?.FindProperty(name) is not null;

    private static int? MaxLength(DbContext context, Type clrType, string name) =>
        context.Model.FindEntityType(clrType)!.FindProperty(name)!.GetMaxLength();

    /// <summary>
    /// Every mapped table, column and column length in one sorted list, so two models can be compared as a
    /// whole rather than one assertion at a time.
    /// </summary>
    private static IReadOnlyList<string> Shape(DbContext context) =>
        [.. context.Model
            .GetEntityTypes()
            .SelectMany(entityType =>
                entityType
                    .GetProperties()
                    .Select(property =>
                        $"{entityType.Name}[{entityType.GetTableName()}].{property.Name}:{property.GetMaxLength()}"))
            .Order(StringComparer.Ordinal)];

    // ------------------------------------------------------------------ a gated column

    [Fact]
    public void The_migrations_model_maps_the_gated_column()
    {
        using var context = Build<MigrationsOnlyContext>(true, options => new(options));

        HasProperty(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeTrue();
    }

    [Fact]
    public void The_application_model_does_not_see_the_gated_column()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));

        HasProperty(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Control_the_ungated_column_beside_it_is_in_both_models(bool migrationsModel)
    {
        // Without this, every assertion above would also pass on a model that had lost everything.
        using var context = Build<MigrationsOnlyContext>(migrationsModel, options => new(options));

        HasProperty(context, typeof(Thing), nameof(Thing.Existing)).ShouldBeTrue();
    }

    // ------------------------------------------------------------------- a gated table

    [Fact]
    public void The_migrations_model_maps_the_gated_table()
    {
        using var context = Build<MigrationsOnlyContext>(true, options => new(options));

        HasEntityType(context, typeof(Gadget)).ShouldBeTrue();
    }

    [Fact]
    public void The_application_model_does_not_have_the_gated_table()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));

        HasEntityType(context, typeof(Gadget)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Control_the_ungated_table_beside_it_is_in_both_models(bool migrationsModel)
    {
        using var context = Build<MigrationsOnlyContext>(migrationsModel, options => new(options));

        HasEntityType(context, typeof(Thing)).ShouldBeTrue();
    }

    [Fact]
    public void Touching_the_DbSet_of_a_gated_table_throws()
    {
        // The failure the estate shipped: the model builds, and the first read throws. It is the loudest
        // failure the pattern has, and it is why a gated table is the safer of the two shapes.
        using var context = Build<GatedTableWithDbSetContext>(false, options => new(options));

        var thrown = Should.Throw<InvalidOperationException>(() => context.Widgets.AsNoTracking().ToQueryString());

        thrown.Message.ShouldContain(nameof(Widget));
    }

    // --------------------------------------------------------- the configuration delegate

    [Fact]
    public void The_configuration_delegate_shapes_the_gated_member_in_the_migrations_model()
    {
        using var context = Build<MigrationsOnlyContext>(true, options => new(options));

        MaxLength(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBe(50);
        MaxLength(context, typeof(Gadget), nameof(Gadget.Name)).ShouldBe(30);
        context.Model.FindEntityType(typeof(Gadget))!.GetTableName().ShouldBe("Gadgets");
    }

    [Fact]
    public void The_configuration_delegate_does_not_run_on_the_application_path()
    {
        // Observed through a shadow property the delegate adds rather than a counter: models are cached, so a
        // counter would carry over between contexts and prove nothing about this one.
        using var migrations = Build<IndexInsideGateContext>(true, options => new(options));
        using var application = Build<IndexInsideGateContext>(false, options => new(options));

        HasProperty(migrations, typeof(Thing), IndexInsideGateContext.ConfigureRanMarker).ShouldBeTrue();
        HasProperty(application, typeof(Thing), IndexInsideGateContext.ConfigureRanMarker).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Omitting_the_configuration_delegate_still_gates_both_shapes(bool migrationsModel)
    {
        using var context = Build<MigrationsOnlyWithoutConfigureContext>(migrationsModel, options => new(options));

        HasProperty(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBe(migrationsModel);
        HasEntityType(context, typeof(Gadget)).ShouldBe(migrationsModel);
    }

    // ------------------------------------------------------------- placement no longer matters
    //
    // The claim the package is making. Under the hand-written gate, moving the Ignore above the mapping it is
    // meant to hide silently loses the member - which the two contexts kept in MigrationsModelExtensionsTests
    // still record. Here the same move is a no-op, because the mapping and the ignore are one call.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Placement_of_the_gate_does_not_change_either_model(bool migrationsModel)
    {
        using var gateLast = Build<MigrationsOnlyContext>(migrationsModel, options => new(options));
        using var gateFirst = Build<MigrationsOnlyGateFirstContext>(migrationsModel, options => new(options));

        // Same shape, allowing for the two context types naming their entity types identically.
        Shape(gateFirst).ShouldBe(Shape(gateLast));
    }

    [Fact]
    public void The_migrations_model_is_identical_to_the_model_release_two_builds()
    {
        // Why release two needs no migration: the gates were the only difference, and they never touched the
        // migrations model. If this fails, the configuration delegate is being applied differently from the
        // ordinary call and release two would generate a migration nobody expects.
        using var migrations = Build<MigrationsOnlyContext>(true, options => new(options));
        using var releaseTwo = Build<ReleaseTwoContext>(false, options => new(options));

        Shape(releaseTwo).ShouldBe(Shape(migrations));
    }

    // ---------------------------------------------------------------- EF's own diagnostics

    [Fact]
    public void The_gate_does_not_report_that_a_mapped_member_was_ignored()
    {
        // Nothing is mapped on the application path, so there is nothing for EF to complain about. The
        // hand-written idiom maps and then ignores, which is what the control below shows.
        using var context = MappedThenIgnoredIsAnError<MigrationsOnlyContext>(options => new(options));

        Should.NotThrow(() => context.Model);
    }

    [Fact]
    public void Control_the_hand_written_gate_does_report_that_a_mapped_member_was_ignored()
    {
        using var context = MappedThenIgnoredIsAnError<GatedContext>(options => new(options));

        Should.Throw<InvalidOperationException>(() => context.Model);
    }

    private static TContext MappedThenIgnoredIsAnError<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext =>
        create(
            new DbContextOptionsBuilder<TContext>()
                .UseSqlServer(ConnectionString)
                .ConfigureWarnings(warnings => warnings.Throw(
                    CoreEventId.MappedPropertyIgnoredWarning,
                    CoreEventId.MappedEntityTypeIgnoredWarning))
                .Options);

    // ------------------------------------------------------- what a gate still cannot own
    //
    // These record EF behaviour rather than this package's. Both are the same rule seen twice: an explicit
    // mapping of a gated member from outside the gate un-ignores it, and the gate cannot see calls it did not
    // make. They are the reason the overload taking a list of members exists.

    [Fact]
    public void An_index_declared_after_the_gate_puts_the_column_back()
    {
        using var context = Build<IndexAfterGateContext>(false, options => new(options));

        // HasIndex maps the property to reach it, and that mapping overrides the ignore. Nothing warns.
        HasProperty(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeTrue();
    }

    [Fact]
    public void An_index_declared_inside_the_gate_stays_out_of_the_application_model()
    {
        using var migrations = Build<IndexInsideGateContext>(true, options => new(options));
        using var application = Build<IndexInsideGateContext>(false, options => new(options));

        HasProperty(migrations, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeTrue();
        migrations.Model.FindEntityType(typeof(Thing))!.GetIndexes().ShouldNotBeEmpty();

        HasProperty(application, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeFalse();
        application.Model.FindEntityType(typeof(Thing))!.GetIndexes().ShouldBeEmpty();
    }

    [Fact]
    public void Gating_a_column_the_rest_of_the_model_already_mapped_does_nothing_at_all()
    {
        // ⚠️ Verified on EF Core 10.0.11. The relationship above the gate maps CustomerId explicitly, and an
        // explicit mapping cannot be removed by ignoring it afterwards while a foreign key is still using it -
        // so the gate is a no-op and the application model keeps the column, the foreign key and the
        // navigation. The member is not renamed, shadowed or half-removed; nothing happens.
        using var context = Build<ForeignKeyBeforeGateContext>(false, options => new(options));

        var order = context.Model.FindEntityType(typeof(Order))!;
        var property = order.FindProperty(nameof(Order.CustomerId)).ShouldNotBeNull();

        property.IsShadowProperty().ShouldBeFalse();
        order.GetForeignKeys().ShouldHaveSingleItem().Properties.ShouldHaveSingleItem().ShouldBe(property);
        order.FindNavigation(nameof(Order.Customer)).ShouldNotBeNull();
    }

    [Fact]
    public void EF_reports_the_gate_that_did_nothing_as_a_mapped_member_that_was_ignored()
    {
        // The reason the warning escalation is worth switching on in an application: this is the one entangled
        // shape the gate cannot detect for itself, and EF names the exact member.
        using var context = MappedThenIgnoredIsAnError<ForeignKeyBeforeGateContext>(options => new(options));

        var thrown = Should.Throw<InvalidOperationException>(() => context.Model);

        thrown.Message.ShouldContain(nameof(Order.CustomerId));
    }

    [Fact]
    public void Nothing_reports_the_index_that_put_the_column_back()
    {
        // ⚠️ The mirror image, and the residual hole: configuration that maps a gated member AFTER the gate is
        // silent even with every ignore-related warning escalated, because from EF's point of view nothing was
        // ignored and then mapped - it was mapped, full stop. What catches it is the database: the application
        // queries a column the migration has not created yet.
        using var context = MappedThenIgnoredIsAnError<IndexAfterGateContext>(options => new(options));

        Should.NotThrow(() => context.Model);
        HasProperty(context, typeof(Thing), nameof(Thing.NewColumn)).ShouldBeTrue();
    }

    [Fact]
    public void Gating_a_foreign_key_together_with_its_navigation_removes_the_whole_relationship()
    {
        using var migrations = Build<GatedForeignKeyContext>(true, options => new(options));
        using var application = Build<GatedForeignKeyContext>(false, options => new(options));

        HasProperty(migrations, typeof(Order), nameof(Order.CustomerId)).ShouldBeTrue();
        migrations.Model.FindEntityType(typeof(Order))!.GetForeignKeys().ShouldNotBeEmpty();

        HasProperty(application, typeof(Order), nameof(Order.CustomerId)).ShouldBeFalse();
        application.Model.FindEntityType(typeof(Order))!.FindNavigation(nameof(Order.Customer)).ShouldBeNull();
        application.Model.FindEntityType(typeof(Order))!.GetForeignKeys().ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- argument guards

    [Fact]
    public void The_table_gate_rejects_a_null_builder_and_a_null_subject()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));
        var modelBuilder = new ModelBuilder();

        Should.Throw<ArgumentNullException>(() => ((ModelBuilder)null!).MigrationsOnly<Gadget>(context));
        Should.Throw<ArgumentNullException>(() => modelBuilder.MigrationsOnly<Gadget>((DbContext)null!));
        Should.Throw<ArgumentNullException>(() => modelBuilder.MigrationsOnly<Gadget>((DbContextOptions)null!));
    }

    [Fact]
    public void The_column_gate_rejects_a_null_builder_and_a_null_subject()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));
        var thing = new ModelBuilder().Entity<Thing>();

        Should.Throw<ArgumentNullException>(
            () => ((EntityTypeBuilder<Thing>)null!).MigrationsOnly(context, entity => entity.NewColumn));
        Should.Throw<ArgumentNullException>(
            () => thing.MigrationsOnly((DbContext)null!, entity => entity.NewColumn));
        Should.Throw<ArgumentNullException>(
            () => thing.MigrationsOnly((DbContextOptions)null!, entity => entity.NewColumn));
        Should.Throw<ArgumentNullException>(
            () => thing.MigrationsOnly<Thing, string?>(context, null!));
    }

    [Fact]
    public void The_members_gate_rejects_an_empty_list()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));
        var thing = new ModelBuilder().Entity<Thing>();

        // A call that gates nothing is a call that only configures the migrations model, which is the
        // divergence this package exists to prevent rather than to offer.
        Should.Throw<ArgumentException>(() => thing.MigrationsOnly(context, []));
    }

    [Fact]
    public void A_gate_rejects_an_expression_that_is_not_a_plain_member_access()
    {
        using var context = Build<MigrationsOnlyContext>(false, options => new(options));
        var thing = new ModelBuilder().Entity<Thing>();

        Should.Throw<ArgumentException>(
            () => thing.MigrationsOnly(context, entity => entity.Existing!.Length));
    }

    [Fact]
    public void Passing_a_navigation_to_the_column_gate_is_rejected()
    {
        // It would otherwise map on one path and ignore on the other - a difference between the two models
        // produced by the gate itself, which is the one thing the gate must never do.
        using var context = Build<GatedForeignKeyContext>(false, options => new(options));

        var order = new ModelBuilder().Entity<Order>();
        order.HasOne(entity => entity.Customer).WithMany().HasForeignKey(entity => entity.CustomerId);

        var thrown = Should.Throw<ArgumentException>(
            () => order.MigrationsOnly(context, entity => entity.Customer));

        thrown.Message.ShouldContain(nameof(Order.Customer));
    }
}
