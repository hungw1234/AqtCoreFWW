using System.Reflection;
using AqtCoreFW.Api;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// R1 boot proof (phase-05 tests spec): drives the REAL <c>E2ETestDataSeeder</c> permission-catalog
/// + role-grant code (not a hand-rolled substitute) against a real (TestContainers) IdentityDbContext,
/// then asserts SystemAdmin AND Admin actually end up with a <c>RolePermission</c> row for
/// <see cref="PermCodes.Forms.WorkflowLink.Manage"/>.
///
/// Why reflection: <c>E2ETestDataSeeder.StartAsync</c> gates the whole seed pipeline on
/// <c>env.IsDevelopment()</c> (never true under ASPNETCORE_ENVIRONMENT=Testing — see this project's
/// TestEffectivePermissionService doc-comment), and the two steps this test needs
/// (<c>SeedPermissionsAsync</c> / <c>SeedRolePermissionsAsync</c>) are <c>private static</c> — even
/// <c>InternalsVisibleTo</c> does not reach private members. Invoking them directly via reflection
/// against the harness's IdentityDbContext exercises the ACTUAL shipped seeding logic (catalog +
/// exclusion-list role-grant), not a reimplementation that could silently drift from production.
///
/// This test seeds ONLY the 3 roles the grant step reads (SystemAdmin/Admin/GovOfficer) — it
/// deliberately skips <c>SeedAdditionalUsersAsync</c> (user creation, Vault-backed passwords) since
/// that machinery is orthogonal to the R1 permission-catalog risk this test targets.
/// </summary>
[Collection("Integration")]
public sealed class WorkflowLinkManagePermGrantBootTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static async Task InvokePrivateStaticAsync(string methodName, IServiceProvider sp, CancellationToken ct)
    {
        var method = typeof(E2ETestDataSeeder).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(E2ETestDataSeeder), methodName);

        var task = (Task)method.Invoke(null, [sp, ct])!;
        await task;
    }

    [Fact]
    public async Task SystemAdmin_And_Admin_Roles_Are_Granted_WorkflowLinkManage_After_RealSeederRuns()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db2 = sp.GetRequiredService<IdentityDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var ct = CancellationToken.None;

        // Seed the 3 system roles the grant step reads — mirrors E2ETestDataSeeder.SeedAdditionalUsersAsync's
        // role-creation shape (Code/RoleType/IsActive) without invoking its user/password machinery.
        foreach (var (name, code) in new[] { ("SystemAdmin", "SYSTEM_ADMIN"), ("Admin", "ADMIN"), ("GovOfficer", "GOV_OFFICER") })
        {
            if (await roleManager.FindByNameAsync(name) is null)
                await roleManager.CreateAsync(new ApplicationRole { Name = name, Code = code, RoleType = RoleType.System, IsActive = true });
        }

        // Drive the REAL seeder code — catalog then role-grant (grant step reads Permissions table).
        await InvokePrivateStaticAsync("SeedPermissionsAsync", sp, ct);
        await InvokePrivateStaticAsync("SeedRolePermissionsAsync", sp, ct);

        var systemAdminRoleId = (await roleManager.FindByNameAsync("SystemAdmin"))!.Id;
        var adminRoleId = (await roleManager.FindByNameAsync("Admin"))!.Id;

        var permissionId = await db2.Permissions
            .Where(p => p.Code == PermCodes.Forms.WorkflowLink.Manage)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        permissionId.Should().NotBeNull(
            "SeedPermissionsAsync must have inserted a Permission row for FORMS.WFLINK.MANAGE (R1 catalog half)");

        var systemAdminHasIt = await db2.RolePermissions
            .AnyAsync(rp => rp.RoleId == systemAdminRoleId && rp.PermissionId == permissionId, ct);
        var adminHasIt = await db2.RolePermissions
            .AnyAsync(rp => rp.RoleId == adminRoleId && rp.PermissionId == permissionId, ct);

        systemAdminHasIt.Should().BeTrue("SystemAdmin is granted ALL permissions by SeedRolePermissionsAsync");
        adminHasIt.Should().BeTrue(
            "Admin is granted all permissions except an explicit exclusion list, and " +
            "FORMS.WFLINK.MANAGE must NOT be added to that exclusion list (R1 — perm-unassignable risk)");
    }
}
