using Likvido.EfCore.MigrationsModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Shouldly;
using Xunit;

namespace Likvido.EfCore.MigrationsModel.Tests;

/// <summary>
/// The property under test throughout: <c>UseMigrationsModel()</c> makes a context build its complete
/// model, so a gated <c>Ignore</c> hides a column from application code without hiding it from migrations.
/// </summary>
public class MigrationsModelExtensionsTests
{
    private const string ConnectionString = "Server=localhost;Database=tests;Trusted_Connection=True;";

    private static GatedContext Gated(bool migrationsModel)
    {
        var builder = new DbContextOptionsBuilder<GatedContext>().UseSqlServer(ConnectionString);

        if (migrationsModel)
        {
            builder.UseMigrationsModel();
        }

        return new GatedContext(builder.Options);
    }

    private static bool HasNewColumn(DbContext context) =>
        context.Model.FindEntityType(typeof(Thing))!.FindProperty(nameof(Thing.NewColumn)) is not null;

    [Fact]
    public void The_flag_is_off_unless_it_is_asked_for()
    {
        using var context = Gated(migrationsModel: false);

        context.IsMigrationsModel().ShouldBeFalse();
    }

    [Fact]
    public void The_flag_is_on_when_the_options_opt_in()
    {
        using var context = Gated(migrationsModel: true);

        context.IsMigrationsModel().ShouldBeTrue();
    }

    [Fact]
    public void The_migrations_model_keeps_the_column_application_code_ignores()
    {
        using var context = Gated(migrationsModel: true);

        HasNewColumn(context).ShouldBeTrue();
    }

    [Fact]
    public void The_application_model_does_not_see_the_column()
    {
        using var context = Gated(migrationsModel: false);

        HasNewColumn(context).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_options_overload_agrees_with_the_context_overload(bool migrationsModel)
    {
        var builder = new DbContextOptionsBuilder<GatedContext>().UseSqlServer(ConnectionString);

        if (migrationsModel)
        {
            builder.UseMigrationsModel();
        }

        using var context = new GatedContext(builder.Options);

        builder.Options.IsMigrationsModel().ShouldBe(migrationsModel);
        context.IsMigrationsModel().ShouldBe(migrationsModel);
    }

    [Fact]
    public void Opting_in_twice_is_the_same_as_once()
    {
        var options = new DbContextOptionsBuilder<GatedContext>()
            .UseSqlServer(ConnectionString)
            .UseMigrationsModel()
            .UseMigrationsModel()
            .Options;

        using var context = new GatedContext(options);

        context.IsMigrationsModel().ShouldBeTrue();
        HasNewColumn(context).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- the model cache
    //
    // One context type now produces two models, so the cache has to keep them apart. These tests are the
    // reason this package needs no IModelCacheKeyFactory of its own: EF keys its internal service provider
    // on the SET of options extensions, so the marker's presence already puts the two variants in separate
    // caches. The control test is what stops that being a vacuous claim.

    [Fact]
    public void Control_two_identical_contexts_share_one_cached_model()
    {
        using var first = Gated(migrationsModel: false);
        using var second = Gated(migrationsModel: false);

        // If this ever fails, caching is not happening and the separation tests below prove nothing.
        second.Model.ShouldBeSameAs(first.Model);
    }

    [Fact]
    public void Control_two_identical_migrations_contexts_share_one_cached_model()
    {
        using var first = Gated(migrationsModel: true);
        using var second = Gated(migrationsModel: true);

        second.Model.ShouldBeSameAs(first.Model);
    }

    [Fact]
    public void The_two_models_are_cached_separately()
    {
        using var migrations = Gated(migrationsModel: true);
        using var application = Gated(migrationsModel: false);

        application.Model.ShouldNotBeSameAs(migrations.Model);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Neither_model_poisons_the_other_whichever_is_built_first(bool migrationsFirst)
    {
        var order = migrationsFirst ? new[] { true, false, true, false } : [false, true, false, true];

        foreach (var migrationsModel in order)
        {
            using var context = Gated(migrationsModel);

            HasNewColumn(context).ShouldBe(migrationsModel);
            context.IsMigrationsModel().ShouldBe(migrationsModel);
        }
    }

    // ---------------------------------------------------------- what the gate protects against
    //
    // These two record EF behaviour rather than this package's, and they are here because both cost the
    // estate a real column. They fail if EF ever changes, which is the point.

    [Fact]
    public void An_ungated_ignore_hides_the_column_from_the_migrations_model_too()
    {
        var options = new DbContextOptionsBuilder<UngatedContext>()
            .UseSqlServer(ConnectionString)
            .UseMigrationsModel()
            .Options;

        using var context = new UngatedContext(options);

        // The opt-in cannot rescue an unconditional Ignore: the migrations model is missing the column, so
        // the next migration generated from it emits a DropColumn for a column the application still needs.
        context.IsMigrationsModel().ShouldBeTrue();
        HasNewColumn(context).ShouldBeFalse();
    }

    [Fact]
    public void An_ignore_placed_before_the_property_mapping_is_silently_undone()
    {
        var options = new DbContextOptionsBuilder<IgnoreBeforeMappingContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var context = new IgnoreBeforeMappingContext(options);

        // Nothing warns about this. The ignore reads as if it applied, and the column stays mapped.
        HasNewColumn(context).ShouldBeTrue();
    }

    // ------------------------------------------------------------------- argument guards

    [Fact]
    public void UseMigrationsModel_rejects_a_null_builder()
    {
        Should.Throw<ArgumentNullException>(
            () => ((DbContextOptionsBuilder)null!).UseMigrationsModel());

        Should.Throw<ArgumentNullException>(
            () => ((DbContextOptionsBuilder<GatedContext>)null!).UseMigrationsModel());
    }

    [Fact]
    public void IsMigrationsModel_rejects_a_null_subject()
    {
        Should.Throw<ArgumentNullException>(() => ((DbContext)null!).IsMigrationsModel());
        Should.Throw<ArgumentNullException>(() => ((DbContextOptions)null!).IsMigrationsModel());
    }

    [Fact]
    public void The_generic_overload_returns_the_same_builder_so_it_can_be_chained()
    {
        var builder = new DbContextOptionsBuilder<GatedContext>();

        builder.UseMigrationsModel().ShouldBeSameAs(builder);
    }

    [Fact]
    public void The_marker_is_visible_in_the_debug_info_that_EF_logs()
    {
        var options = new DbContextOptionsBuilder<GatedContext>()
            .UseSqlServer(ConnectionString)
            .UseMigrationsModel()
            .Options;

        var debugInfo = new Dictionary<string, string>();

        foreach (var extension in options.Extensions)
        {
            extension.Info.PopulateDebugInfo(debugInfo);
        }

        debugInfo.ShouldContainKeyAndValue("Likvido:MigrationsModel", "1");
    }
}
