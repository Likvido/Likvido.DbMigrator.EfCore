using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Likvido.EfCore.MigrationsModel;

/// <summary>
/// Marks a set of <see cref="DbContextOptions"/> as belonging to the migration path, so a context can
/// build its <em>complete</em> model there and its application model everywhere else.
/// <para>
/// It contributes no services and changes no behaviour on its own — its only job is to be findable from
/// inside <c>OnModelCreating</c>. See <see cref="MigrationsModelExtensions"/> for what a caller uses.
/// </para>
/// <para>
/// ⚠️ <strong>Its presence is what separates the two models in EF's cache, and that is load-bearing.</strong>
/// EF keys its internal service provider on the <em>set</em> of options extensions, so options carrying this
/// extension resolve a different provider — and therefore a different model cache — from options without it.
/// That is why one context type can produce two models in a single process without an
/// <c>IModelCacheKeyFactory</c> of its own. Verified on EF Core 10.0.11 by building both variants in one
/// process, in both orders and repeatedly, and confirming each got its own <c>IModel</c> instance while two
/// identical variants shared one.
/// </para>
/// </summary>
internal sealed class MigrationsModelExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => field ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        // Nothing. The extension is a marker, not a service.
    }

    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: the marker is valid wherever it is set.
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        // Reaches the log line EF writes when it initialises the context, so a migration run says which
        // model it built rather than leaving it to be inferred.
        public override string LogFragment => "using the migrations model ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other.Extension is MigrationsModelExtension;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Likvido:MigrationsModel"] = "1";
    }
}
