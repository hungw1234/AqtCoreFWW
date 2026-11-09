using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Enforces the ADR-010 invariant: the set of DbContexts the AuthServer host migrates at
/// startup MUST stay a strict subset of the Api host's startup-migrate set.
///
/// Why it matters: if AuthServer migrated a context Api does NOT, AuthServer would become the
/// sole schema owner for that context — but AuthServer's runtime DB login is intentionally
/// least-privileged (no db_ddladmin), so on a least-privilege deploy that schema would never
/// get created/updated. The two hosts declare their migrate sets as data
/// (<c>StartupMigrationSets.Contexts</c>) precisely so this relationship is machine-checkable
/// instead of a brittle text-scan of two Program.cs files.
/// </summary>
public sealed class MigrateSetSubsetGuardTests
{
    private static IReadOnlyList<Type> ApiSet => AqtCoreFW.Api.StartupMigrationSets.Contexts;
    private static IReadOnlyList<Type> AuthServerSet => AqtCoreFW.AuthServer.StartupMigrationSets.Contexts;

    [Fact]
    public void AuthServerStartupMigrateSet_IsSubsetOfApiStartupMigrateSet()
    {
        // The load-bearing invariant. Compute the unaccounted contexts explicitly so a failure
        // names exactly which context broke the subset relationship.
        var unaccounted = AuthServerSet.Where(t => !ApiSet.Contains(t)).ToList();

        unaccounted.Should().BeEmpty(
            "ADR-010: every context AuthServer migrates at startup must also be in Api's migrate " +
            "set, else AuthServer becomes the sole schema owner for a context whose runtime login " +
            "is intentionally least-privileged");
    }

    [Fact]
    public void StartupMigrateSets_AreNonEmptyAndAllDbContexts()
    {
        // Anti-vacuous: a subset check over an empty AuthServer set would pass trivially.
        AuthServerSet.Should().NotBeEmpty();
        ApiSet.Should().NotBeEmpty();

        AuthServerSet.Concat(ApiSet).Should().OnlyContain(
            t => typeof(DbContext).IsAssignableFrom(t),
            "the migrate sets must list DbContext types, not arbitrary types");
    }

    [Fact]
    public void ApiStartupMigrateSet_HasNoDuplicateContexts()
    {
        // A duplicated context would migrate twice (harmless but a copy-paste smell) and would
        // skew any future subset/coverage reasoning over the set.
        ApiSet.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AuthServerStartupMigrateSet_IsExactlyIdentityAndMessaging()
    {
        // Tripwire: AuthServer is intentionally lean (ADR-010). If this set grows, the change
        // must be a conscious decision — verify the new context genuinely needs migrating on the
        // least-privileged AuthServer host rather than being owned by Api/Helm, then update this
        // assertion. The subset guard above stays the real safety net.
        AuthServerSet.Should().BeEquivalentTo(new[]
        {
            typeof(IdentityDbContext),
            typeof(MessagingDbContext),
        });
    }
}
