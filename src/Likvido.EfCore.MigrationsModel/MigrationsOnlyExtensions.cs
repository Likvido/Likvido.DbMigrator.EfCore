using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Likvido.EfCore.MigrationsModel;

/// <summary>
/// The expand half of expand/contract, as one call: a table or column that the migrations model maps and the
/// application model does not know exists.
/// <para>
/// <strong>Why the package owns this rather than documenting it.</strong> The same thing can be written by
/// hand as an <c>Ignore</c> inside <c>if (!this.IsMigrationsModel())</c>, and there are three ways to write
/// that which compile, read correctly in review, and lose the column: leaving the <c>Ignore</c> ungated,
/// inverting the condition, and placing the <c>Ignore</c> above the mapping it is meant to hide — where the
/// later mapping silently puts the member back. None of the three is reachable through these methods, because
/// the mapping and the ignore are the same call and the call decides the order.
/// </para>
/// <example>
/// A new table, and a new column on an existing one:
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     modelBuilder.MigrationsOnly&lt;Gadget&gt;(this, gadget =&gt;
///     {
///         gadget.ToTable("Gadgets");
///         gadget.HasKey(entity =&gt; entity.Id);
///     });
///
///     modelBuilder.Entity&lt;Thing&gt;(thing =&gt;
///     {
///         thing.HasKey(entity =&gt; entity.Id);
///         thing.MigrationsOnly(this, entity =&gt; entity.NewColumn, column =&gt; column.HasMaxLength(50));
///     });
/// }
/// </code>
/// The next release replaces each call with the ordinary EF one — <c>Entity&lt;Gadget&gt;(gadget =&gt; …)</c>
/// and <c>thing.Property(entity =&gt; entity.NewColumn).HasMaxLength(50)</c> — and needs no migration, because
/// the migrations model already described both. A removal runs the same edit backwards: turn the ordinary call
/// into a <c>MigrationsOnly</c> one in the release that stops using the member, then delete it and generate
/// the drop in the release after. Everything still outstanding in a repository is therefore one grep for
/// <c>MigrationsOnly</c>. Nothing here enforces that the second release happens — that remains something to
/// remember — but it is at least countable.
/// </example>
/// <para>
/// ⚠️ <strong>What placement-independence actually buys, stated precisely.</strong> These methods make the
/// order of the calls they make irrelevant, which is why the <c>Ignore</c>-above-the-mapping mistake cannot be
/// written. They cannot own the order of calls they never see: <em>every</em> mention of a gated member — its
/// facets, its index, its key, its relationship, its seed data — has to be inside a gated call. An explicit
/// mapping of a gated member anywhere else puts it straight back into the application model, and silently,
/// because EF treats a later explicit mapping as un-ignoring the member. The overload taking a list of members
/// exists so that the awkward cases — an index on a gated column, a foreign key and its navigation — have
/// somewhere to go.
/// </para>
/// <para>
/// <strong>Switch the two ignore warnings into errors and the one blind spot closes.</strong> A healthy
/// application model built through these methods maps nothing before ignoring it, so
/// <c>MappedPropertyIgnoredWarning</c> and <c>MappedEntityTypeIgnoredWarning</c> never fire — which leaves
/// them free to mean something. Both fire exactly when a gated member was already mapped by configuration
/// outside its gate, the case above that the gate cannot see, and both also fire on a gate written by hand,
/// which maps and then ignores:
/// <code>
/// options.ConfigureWarnings(warnings =&gt; warnings.Throw(
///     CoreEventId.MappedPropertyIgnoredWarning,
///     CoreEventId.MappedEntityTypeIgnoredWarning));
/// </code>
/// Verified on EF Core 10.0.11. It does not catch configuration placed <em>after</em> a gate, which maps the
/// member without ignoring anything and so is not a shape EF has an opinion about; what catches that is the
/// first query against a column the migration has not created yet.
/// </para>
/// <para>
/// Configuration that has to <em>differ</em> between the two models rather than be absent from one of them — a
/// global query filter over a gated column is the real example — is not something these methods express.
/// <see cref="MigrationsModelExtensions.IsMigrationsModel(DbContext)"/> stays public for exactly that, and a
/// filter simply moved inside a gate would leave the application with no filter at all.
/// </para>
/// </summary>
public static class MigrationsOnlyExtensions
{
    /// <summary>
    /// Maps <typeparamref name="TEntity"/> in the migrations model and hides it from the application model,
    /// for a table being shipped a release ahead of the code that reads it.
    /// <para>
    /// <paramref name="configure"/> runs on the migration path only. The application path has no mapped
    /// entity type to configure, and mapping it there in order to ignore it again is both pointless and the
    /// shape EF reports as <c>MappedEntityTypeIgnoredWarning</c>.
    /// </para>
    /// <para>
    /// ⚠️ <strong>Nothing outside <paramref name="configure"/> may name the gated type.</strong> A
    /// relationship, an index or a <c>HasData</c> call elsewhere maps it again on the application path, which
    /// undoes the gate without saying so. That includes a navigation <em>to</em> the gated type from a mapped
    /// entity: EF discovers it and brings the type back, so hold the navigation property back until the
    /// release that uses the table, exactly as you would hold back a column.
    /// </para>
    /// <para>
    /// ⚠️ <strong>A <c>DbSet&lt;TEntity&gt;</c> property belongs to the release that uses the table, not the
    /// release that ships it.</strong> Declaring one while the type is gated compiles and builds a model;
    /// touching it throws <em>"Cannot create a DbSet for 'TEntity' because this type is not included in the
    /// model for the context"</em> on first use.
    /// </para>
    /// <para>
    /// ⚠️ <strong>Not for a type in an inheritance hierarchy.</strong> Ignoring a base type rebases its
    /// derived types onto their grandparent, and a discriminator value configured on the base resurrects a
    /// gated derived type. Gate the columns instead.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The builder <c>OnModelCreating</c> was handed.</param>
    /// <param name="context">The context being built — pass <c>this</c>.</param>
    /// <param name="configure">
    /// The table's own configuration, exactly as it would be written inside <c>Entity&lt;TEntity&gt;()</c>.
    /// Omit it for a table EF's conventions already map correctly.
    /// </param>
    public static ModelBuilder MigrationsOnly<TEntity>(
        this ModelBuilder modelBuilder,
        DbContext context,
        Action<EntityTypeBuilder<TEntity>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);

        return MigrationsOnlyEntity(modelBuilder, context.IsMigrationsModel(), configure);
    }

    /// <inheritdoc cref="MigrationsOnly{TEntity}(ModelBuilder, DbContext, Action{EntityTypeBuilder{TEntity}})"/>
    /// <remarks>
    /// For a context that would rather capture the answer in its constructor, and for an
    /// <c>IEntityTypeConfiguration&lt;T&gt;</c>, which is handed a builder and never the context.
    /// </remarks>
    public static ModelBuilder MigrationsOnly<TEntity>(
        this ModelBuilder modelBuilder,
        DbContextOptions options,
        Action<EntityTypeBuilder<TEntity>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);

        return MigrationsOnlyEntity(modelBuilder, options.IsMigrationsModel(), configure);
    }

    /// <summary>
    /// Maps one property in the migrations model and hides it from the application model, for a column being
    /// shipped a release ahead of the code that writes it.
    /// <para>
    /// <paramref name="configure"/> runs on the migration path only, for the same reason as the table
    /// overload: the application model has no mapped property to configure.
    /// </para>
    /// <para>
    /// ⚠️ <strong>A gated column may not be named by a key, an index or a foreign key configured outside this
    /// call.</strong> If that configuration is <em>above</em> the gate, the column is already mapped
    /// explicitly and the gate does nothing at all — no exception, no partial removal, the application model
    /// simply keeps the column. EF does report it, as
    /// <c>MappedPropertyIgnoredWarning</c> naming the member, which is why escalating that warning is worth
    /// doing. If it is <em>below</em> the gate, the column comes back and nothing is reported. Either way, use
    /// the overload taking a list of members, which puts all of it inside the gate.
    /// </para>
    /// <para>
    /// ⚠️ <strong>Unlike a gated table, a gated column is silent when it outlives its release.</strong>
    /// <c>Set&lt;T&gt;()</c> throws for a table the model does not have; a property the model does not have
    /// still exists on the class, so reads return the default and writes are never persisted. Adding a column
    /// is the more common change, so the quieter failure is also the more likely one.
    /// </para>
    /// </summary>
    /// <param name="entityTypeBuilder">The builder for the entity the column belongs to.</param>
    /// <param name="context">The context being built — pass <c>this</c>.</param>
    /// <param name="propertyExpression">
    /// A plain property access on the entity, such as <c>entity =&gt; entity.NewColumn</c>.
    /// </param>
    /// <param name="configure">
    /// The column's own configuration, exactly as it would be chained onto <c>Property(...)</c>. Omit it for a
    /// column EF's conventions already map correctly.
    /// </param>
    public static EntityTypeBuilder<TEntity> MigrationsOnly<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContext context,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        Action<PropertyBuilder<TProperty>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);

        return MigrationsOnlyProperty(entityTypeBuilder, context.IsMigrationsModel(), propertyExpression, configure);
    }

    /// <inheritdoc cref="MigrationsOnly{TEntity, TProperty}(EntityTypeBuilder{TEntity}, DbContext, Expression{Func{TEntity, TProperty}}, Action{PropertyBuilder{TProperty}})"/>
    /// <remarks>
    /// For a context that would rather capture the answer in its constructor, and for an
    /// <c>IEntityTypeConfiguration&lt;T&gt;</c>, which is handed a builder and never the context.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> MigrationsOnly<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContextOptions options,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        Action<PropertyBuilder<TProperty>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);

        return MigrationsOnlyProperty(entityTypeBuilder, options.IsMigrationsModel(), propertyExpression, configure);
    }

    /// <summary>
    /// Hides several members at once, and runs <paramref name="configure"/> against the whole entity on the
    /// migration path — for everything the single-property overload cannot reach, because <c>HasIndex</c>,
    /// <c>HasKey</c>, <c>HasOne</c> and <c>HasData</c> live on the entity builder rather than on a property
    /// builder.
    /// <para>
    /// This is where an index on a gated column belongs, and where a foreign key belongs together with the
    /// navigation that goes with it — both gated, both configured, in one call that cannot be split apart or
    /// reordered.
    /// </para>
    /// <example>
    /// <code>
    /// thing.MigrationsOnly(
    ///     this,
    ///     [entity =&gt; entity.CustomerId, entity =&gt; entity.Customer],
    ///     gated =&gt;
    ///     {
    ///         gated.HasOne(entity =&gt; entity.Customer)
    ///              .WithMany()
    ///              .HasForeignKey(entity =&gt; entity.CustomerId);
    ///
    ///         gated.HasIndex(entity =&gt; entity.CustomerId);
    ///     });
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="entityTypeBuilder">The builder for the entity the members belong to.</param>
    /// <param name="context">The context being built — pass <c>this</c>.</param>
    /// <param name="members">
    /// Plain member accesses on the entity, such as <c>[entity =&gt; entity.NewColumn]</c>. Scalars and
    /// navigations both work; every member named here is hidden from the application model.
    /// </param>
    /// <param name="configure">
    /// Configuration for the gated members, run on the migration path only. Anything it touches that is
    /// <em>not</em> in <paramref name="members"/> keeps whatever the application model gave it, so keep it to
    /// the gated members and what binds them together.
    /// </param>
    public static EntityTypeBuilder<TEntity> MigrationsOnly<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContext context,
        IReadOnlyList<Expression<Func<TEntity, object?>>> members,
        Action<EntityTypeBuilder<TEntity>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);

        return MigrationsOnlyMembers(entityTypeBuilder, context.IsMigrationsModel(), members, configure);
    }

    /// <inheritdoc cref="MigrationsOnly{TEntity}(EntityTypeBuilder{TEntity}, DbContext, IReadOnlyList{Expression{Func{TEntity, object}}}, Action{EntityTypeBuilder{TEntity}})"/>
    /// <remarks>
    /// For a context that would rather capture the answer in its constructor, and for an
    /// <c>IEntityTypeConfiguration&lt;T&gt;</c>, which is handed a builder and never the context.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> MigrationsOnly<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContextOptions options,
        IReadOnlyList<Expression<Func<TEntity, object?>>> members,
        Action<EntityTypeBuilder<TEntity>>? configure = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);

        return MigrationsOnlyMembers(entityTypeBuilder, options.IsMigrationsModel(), members, configure);
    }

    private static ModelBuilder MigrationsOnlyEntity<TEntity>(
        ModelBuilder modelBuilder,
        bool isMigrationsModel,
        Action<EntityTypeBuilder<TEntity>>? configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        if (isMigrationsModel)
        {
            // Entity<TEntity>() first and unconditionally: folded into `configure?.Invoke(...)` it would not
            // run at all when no delegate was given, and the table would be missing from the model that is
            // supposed to have it.
            var entityTypeBuilder = modelBuilder.Entity<TEntity>();

            configure?.Invoke(entityTypeBuilder);

            return modelBuilder;
        }

        // Nothing is mapped first. EF discovers the type from its DbSet property before OnModelCreating runs,
        // but that discovery is not explicit configuration, so ignoring it here removes it outright - and,
        // unlike a map-then-ignore, it does not report MappedEntityTypeIgnoredWarning on every model build.
        modelBuilder.Ignore<TEntity>();

        return modelBuilder;
    }

    private static EntityTypeBuilder<TEntity> MigrationsOnlyProperty<TEntity, TProperty>(
        EntityTypeBuilder<TEntity> entityTypeBuilder,
        bool isMigrationsModel,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        Action<PropertyBuilder<TProperty>>? configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(propertyExpression);

        if (isMigrationsModel)
        {
            // Property(...) first and unconditionally, for the same reason as the table overload: inside
            // `configure?.Invoke(...)` it would be skipped whenever no delegate was given.
            var propertyBuilder = entityTypeBuilder.Property(propertyExpression);

            configure?.Invoke(propertyBuilder);

            return entityTypeBuilder;
        }

        var name = MemberName(propertyExpression);

        // A navigation reaches this overload as a TProperty that happens to be an entity type, and the
        // migration path would then fail inside Property() with EF's own message while the application path
        // quietly succeeded - a difference between the two models produced by the gate itself.
        if (entityTypeBuilder.Metadata.FindNavigation(name) is not null
            || entityTypeBuilder.Metadata.FindSkipNavigation(name) is not null)
        {
            throw new ArgumentException(
                $"'{name}' is a navigation, not a column, so it needs the overload that takes a list of "
                + "members and configures the relationship on the migration path. Gate the foreign key "
                + "property in the same call.",
                nameof(propertyExpression));
        }

        // By name rather than by the expression: the Ignore overload taking one wants
        // Expression<Func<TEntity, object?>>, while Property() wants the typed expression that keeps
        // PropertyBuilder<TProperty> typed for the caller. One of the two has to give, and this is the half
        // that costs nothing.
        entityTypeBuilder.Ignore(name);

        return entityTypeBuilder;
    }

    private static EntityTypeBuilder<TEntity> MigrationsOnlyMembers<TEntity>(
        EntityTypeBuilder<TEntity> entityTypeBuilder,
        bool isMigrationsModel,
        IReadOnlyList<Expression<Func<TEntity, object?>>> members,
        Action<EntityTypeBuilder<TEntity>>? configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        ArgumentNullException.ThrowIfNull(members);

        if (members.Count == 0)
        {
            throw new ArgumentException(
                "Name at least one member to hide from the application model. A call that gates nothing "
                + "configures the migrations model alone, which puts the two models out of step in the "
                + "direction this package exists to prevent.",
                nameof(members));
        }

        // Every name first, so a bad expression is reported before half the members have been ignored.
        var names = new string[members.Count];

        for (var index = 0; index < members.Count; index++)
        {
            names[index] = MemberName(members[index]);
        }

        if (isMigrationsModel)
        {
            configure?.Invoke(entityTypeBuilder);

            return entityTypeBuilder;
        }

        foreach (var name in names)
        {
            entityTypeBuilder.Ignore(name);
        }

        return entityTypeBuilder;
    }

    /// <summary>
    /// The name of the member an <c>entity =&gt; entity.Something</c> expression reads.
    /// </summary>
    private static string MemberName<TEntity, TMember>(Expression<Func<TEntity, TMember>> memberExpression)
    {
        ArgumentNullException.ThrowIfNull(memberExpression);

        var body = memberExpression.Body;

        // A value-typed member reached through a Func<TEntity, object?> arrives wrapped in a conversion the
        // compiler inserted. Nothing else about the expression changes.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            body = conversion.Operand;
        }

        if (body is MemberExpression { Expression.NodeType: ExpressionType.Parameter } member)
        {
            return member.Member.Name;
        }

        throw new ArgumentException(
            "MigrationsOnly needs a plain member access on the entity, such as 'entity => entity.NewColumn'. "
            + $"'{memberExpression}' reaches further than that, and gating a member of an owned or complex "
            + "type is not something these methods do.",
            nameof(memberExpression));
    }
}
