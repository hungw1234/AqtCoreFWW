using AqtCoreFW.Api;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Architecture guard: every PermCodes.All entry MUST be seeded by E2ETestDataSeeder.BuildPermissionList().
/// Drift root cause incident (2026-05-01): T5-10 added WORKFLOW.MIGRATION.* to PermCodes + [Authorize]
/// but seeder was not updated â†’ Permission rows missing â†’ SystemAdmin/Admin got 403 on Versions tab.
/// Same class catches future drift at compile-time of test, before it ships to dev DB.
/// </summary>
public sealed class PermCodesSeederCoverageTests
{
    [Fact]
    public void Every_PermCode_Must_Be_Seeded()
    {
        var seededCodes = E2ETestDataSeeder
            .BuildPermissionList()
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);

        var missing = PermCodes.All
            .Where(code => !seededCodes.Contains(code))
            .OrderBy(c => c)
            .ToList();

        missing.Should().BeEmpty(
            "every PermCodes.All entry must have a matching Permission row seeded â€” " +
            "[Authorize(Policy = ...)] uses these codes and 403s when DB lacks the row. " +
            "Add missing entries to E2ETestDataSeeder.Permissions.BuildPermissionList(). Missing: {0}",
            string.Join(", ", missing));
    }

    [Fact]
    public void Seeder_Must_Not_Have_Codes_Outside_PermCodes_All()
    {
        var permCodesSet = PermCodes.All.ToHashSet(StringComparer.Ordinal);

        var orphaned = E2ETestDataSeeder
            .BuildPermissionList()
            .Select(p => p.Code)
            .Where(code => !permCodesSet.Contains(code))
            .OrderBy(c => c)
            .ToList();

        orphaned.Should().BeEmpty(
            "seeder must not insert Permission rows whose Code is not declared in PermCodes.All â€” " +
            "such rows cannot be referenced by [Authorize(Policy = ...)] (no policy registered). " +
            "Either add to PermCodes.All or remove from BuildPermissionList(). Orphaned: {0}",
            string.Join(", ", orphaned));
    }
}
