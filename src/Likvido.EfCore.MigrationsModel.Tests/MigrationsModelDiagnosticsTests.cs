using Likvido.EfCore.MigrationsModel;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Likvido.EfCore.MigrationsModel.Tests;

/// <summary>
/// The invariant every repository using the pattern should assert once: the migrations model is never missing
/// anything the application model has. What it catches is a gate pointing the wrong way, which
/// <see cref="MigrationsOnlyExtensions"/> makes unwritable but hand-written gates and any other conditional
/// mapping do not.
/// </summary>
public class MigrationsModelDiagnosticsTests
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

    [Fact]
    public void The_diagnostics_name_the_column_an_inverted_gate_hid_from_migrations()
    {
        using var migrations = Build<InvertedGateContext>(true, options => new(options));
        using var application = Build<InvertedGateContext>(false, options => new(options));

        MigrationsModelDiagnostics
            .PropertiesMissingFromMigrationsModel(migrations, application)
            .ShouldBe([$"{typeof(Thing).FullName}.{nameof(Thing.NewColumn)}"]);
    }

    [Fact]
    public void The_diagnostics_name_the_table_an_inverted_gate_hid_from_migrations()
    {
        using var migrations = Build<InvertedTableGateContext>(true, options => new(options));
        using var application = Build<InvertedTableGateContext>(false, options => new(options));

        MigrationsModelDiagnostics
            .EntityTypesMissingFromMigrationsModel(migrations, application)
            .ShouldBe([typeof(Gadget).FullName!]);
    }

    [Fact]
    public void The_diagnostics_are_empty_for_a_correctly_gated_context()
    {
        // The direction that is meant to happen: the migrations model is the superset.
        using var migrations = Build<MigrationsOnlyContext>(true, options => new(options));
        using var application = Build<MigrationsOnlyContext>(false, options => new(options));

        MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
        MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
    }

    [Fact]
    public void Control_the_diagnostics_are_empty_when_nothing_is_gated_at_all()
    {
        // Without this the emptiness above could mean the comparison never looks at anything.
        using var migrations = Build<PlainContext>(true, options => new(options));
        using var application = Build<PlainContext>(false, options => new(options));

        MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
        MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
    }

    [Fact]
    public void The_diagnostics_refuse_a_comparison_that_cannot_fail()
    {
        // Two contexts built the same way agree about everything, and a test asserting that reads exactly like
        // a test asserting something. Both mistakes are worth a throw rather than an empty list.
        using var migrations = Build<InvertedGateContext>(true, options => new(options));
        using var application = Build<InvertedGateContext>(false, options => new(options));

        Should.Throw<ArgumentException>(
            () => MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(application, application));

        Should.Throw<ArgumentException>(
            () => MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(migrations, migrations));
    }

    [Fact]
    public void The_diagnostics_reject_a_null_subject()
    {
        using var context = Build<PlainContext>(true, options => new(options));

        Should.Throw<ArgumentNullException>(
            () => MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(null!, context));

        Should.Throw<ArgumentNullException>(
            () => MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(context, null!));
    }
}
