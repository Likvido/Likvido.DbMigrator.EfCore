using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Likvido.EfCore.MigrationsModel;

/// <summary>
/// The one invariant a repository using expand/contract should assert in its own test suite: <strong>the
/// migrations model is never missing anything the application model has.</strong>
/// <para>
/// A superset rather than an equality, deliberately. It holds whether or not anything is gated at the moment,
/// so adopting or removing a gate never means editing the test — and what it catches is the gate pointing the
/// wrong way, <c>if (this.IsMigrationsModel())</c> written without the <c>!</c>, which leaves migrations blind
/// to a member the application is writing to. <see cref="MigrationsOnlyExtensions"/> makes that particular
/// mistake unreachable, so this is the guard for the hand-written gates that predate it and for anything else
/// that ends up conditioning a mapping.
/// </para>
/// <para>
/// It lives here rather than in each repository's test project because the hand-written version is easy to
/// write too narrowly — comparing the properties of one entity type and reading as though it compared them
/// all.
/// </para>
/// <example>
/// <code>
/// [Fact]
/// public void The_migrations_model_is_never_missing_what_the_application_model_has()
/// {
///     using var migrations = new ThingDesignTimeDbContextFactory().CreateDbContext([]);
///     using var application = ApplicationContext();
///
///     MigrationsModelDiagnostics.EntityTypesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
///     MigrationsModelDiagnostics.PropertiesMissingFromMigrationsModel(migrations, application).ShouldBeEmpty();
/// }
/// </code>
/// </example>
/// <para>
/// The other half of the pattern's coverage is <c>context.Database.HasPendingModelChanges()</c> on the
/// migrations context, which is what catches an ungated ignore — both models lose the member together, so no
/// comparison between them can see it, but the model snapshot still has it. That one needs nothing from this
/// package.
/// </para>
/// </summary>
public static class MigrationsModelDiagnostics
{
    /// <summary>
    /// Entity types the application model maps and the migrations model does not, by EF model name, sorted.
    /// Empty is the healthy answer.
    /// </summary>
    /// <param name="migrationsModel">
    /// A context built through the design-time factory, so with <c>UseMigrationsModel()</c> applied.
    /// </param>
    /// <param name="applicationModel">
    /// A context built the way the application builds it — provider and connection string, nothing else.
    /// </param>
    public static IReadOnlyList<string> EntityTypesMissingFromMigrationsModel(
        DbContext migrationsModel,
        DbContext applicationModel)
    {
        RejectVacuousComparison(migrationsModel, applicationModel);

        var mapped = EntityTypeNames(migrationsModel);

        return [.. EntityTypeNames(applicationModel).Where(name => !mapped.Contains(name)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Properties the application model maps and the migrations model does not, as
    /// <c>EntityType.Property</c>, sorted. Empty is the healthy answer.
    /// <para>
    /// Entity types missing from the migrations model altogether are left to
    /// <see cref="EntityTypesMissingFromMigrationsModel"/> rather than reported once per property here.
    /// Navigations are not compared: a navigation is not something the expand gate hides.
    /// </para>
    /// </summary>
    /// <param name="migrationsModel">
    /// A context built through the design-time factory, so with <c>UseMigrationsModel()</c> applied.
    /// </param>
    /// <param name="applicationModel">
    /// A context built the way the application builds it — provider and connection string, nothing else.
    /// </param>
    public static IReadOnlyList<string> PropertiesMissingFromMigrationsModel(
        DbContext migrationsModel,
        DbContext applicationModel)
    {
        RejectVacuousComparison(migrationsModel, applicationModel);

        var missing = new List<string>();

        foreach (var applicationEntityType in applicationModel.Model.GetEntityTypes())
        {
            var migrationsEntityType = migrationsModel.Model.FindEntityType(applicationEntityType.Name);

            if (migrationsEntityType is null)
            {
                continue;
            }

            missing.AddRange(
                applicationEntityType
                    .GetProperties()
                    .Where(property => migrationsEntityType.FindProperty(property.Name) is null)
                    .Select(property => $"{applicationEntityType.Name}.{property.Name}"));
        }

        return [.. missing.Order(StringComparer.Ordinal)];
    }

    private static HashSet<string> EntityTypeNames(DbContext context) =>
        [.. context.Model.GetEntityTypes().Select(entityType => entityType.Name)];

    /// <summary>
    /// ⚠️ <strong>Two contexts built the same way agree about everything, and a test asserting that reads
    /// exactly like a test asserting something.</strong> The comparison only means anything with one context
    /// from each side, so the arguments being swapped or duplicated is worth a throw rather than an empty
    /// list.
    /// </summary>
    private static void RejectVacuousComparison(DbContext migrationsModel, DbContext applicationModel)
    {
        ArgumentNullException.ThrowIfNull(migrationsModel);
        ArgumentNullException.ThrowIfNull(applicationModel);

        if (!migrationsModel.IsMigrationsModel())
        {
            throw new ArgumentException(
                "This context was not built with UseMigrationsModel(), so it is not the migrations model. "
                + "Build it through the design-time factory, which is the only place that opt-in belongs.",
                nameof(migrationsModel));
        }

        if (applicationModel.IsMigrationsModel())
        {
            throw new ArgumentException(
                "This context was built with UseMigrationsModel(), so it is the migrations model rather than "
                + "the application model. Comparing the migrations model against itself can only pass.",
                nameof(applicationModel));
        }
    }
}
