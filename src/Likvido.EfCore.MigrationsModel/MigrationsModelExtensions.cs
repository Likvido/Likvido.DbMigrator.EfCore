using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Likvido.EfCore.MigrationsModel;

/// <summary>
/// Expand/contract support: lets one <see cref="DbContext"/> class build a <em>complete</em> model on the
/// migration path and an application model everywhere else.
/// <para>
/// <strong>Why this exists.</strong> A deploy gives no guarantee about whether the migration or the new
/// application code lands first, so every schema change has to work with both. A column is therefore shipped
/// to the database in one release with the application code ignoring it, and used in the next — and removals
/// run the cycle backwards. EF6 does this with two contexts, the base one being the migrations model; these
/// two methods are the same idea with one context class.
/// </para>
/// <para>
/// ⚠️ <strong>The ignore must be gated, not unconditional.</strong> An unconditional <c>Ignore</c> takes the
/// column out of the <em>migrations</em> model too, and then the next migration anyone generates — for
/// something completely unrelated — diffs a model without the column against a snapshot with it and emits a
/// <c>DropColumn</c>. Suppressing <c>PendingModelChangesWarning</c> does not prevent that; it only hides the
/// divergence that causes it. Gating the ignore is what keeps the migrations model whole, and a whole
/// migrations model is why nothing here suppresses that warning: it stays on, doing its real job of catching
/// a migration somebody forgot to generate.
/// </para>
/// <example>
/// In the context, with the ignore gated:
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     modelBuilder.Entity&lt;Thing&gt;(thing =>
///     {
///         thing.Property(x => x.NewColumn).HasMaxLength(50);
///
///         // ⚠️ AFTER the property's own mapping, never before it.
///         if (!this.IsMigrationsModel())
///         {
///             thing.Ignore(x => x.NewColumn);
///         }
///     });
/// }
/// </code>
/// In the design-time factory, which only the migrator and <c>dotnet ef</c> ever use:
/// <code>
/// var builder = new DbContextOptionsBuilder&lt;ThingContext&gt;()
///     .UseSqlServer(connectionString)
///     .UseMigrationsModel();
/// </code>
/// </example>
/// </summary>
public static class MigrationsModelExtensions
{
    /// <summary>
    /// Builds the complete model on these options — every property mapped, including the ones application
    /// code ignores.
    /// <para>
    /// ⚠️ <strong>Call this from the design-time factory only.</strong> That factory is what the migrator and
    /// <c>dotnet ef</c> go through and what nothing else uses, which is the whole point: the application
    /// resolves its context from its own options and so keeps the model that matches the code it is running.
    /// Calling it in application startup would give running code columns the database may not have yet.
    /// </para>
    /// </summary>
    public static DbContextOptionsBuilder UseMigrationsModel(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new MigrationsModelExtension());

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseMigrationsModel(DbContextOptionsBuilder)"/>
    public static DbContextOptionsBuilder<TContext> UseMigrationsModel<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        UseMigrationsModel((DbContextOptionsBuilder)optionsBuilder);

        return optionsBuilder;
    }

    /// <summary>
    /// True while this context is building the migrations model. Gate every expand/contract
    /// <c>Ignore</c> on it, and place the gate after the property's own mapping.
    /// <para>
    /// Safe to call from inside <c>OnModelCreating</c>, which is the only place it is meant to be called.
    /// </para>
    /// </summary>
    public static bool IsMigrationsModel(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetService<IDbContextOptions>().FindExtension<MigrationsModelExtension>() is not null;
    }

    /// <summary>
    /// The same answer read straight off the options, for a context that would rather capture it in its
    /// constructor than call <see cref="IsMigrationsModel(DbContext)"/> during model building.
    /// </summary>
    public static bool IsMigrationsModel(this DbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.FindExtension<MigrationsModelExtension>() is not null;
    }
}
