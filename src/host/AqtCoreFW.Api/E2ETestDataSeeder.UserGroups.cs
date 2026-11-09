using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Api;

/// <summary>
/// Seeds user groups and memberships for RT2-15 Phase 02 ByGroup integration tests.
///
/// Groups (2):
///   GROUP_TEST_A  — active group; 3 memberships (alice active, bob active, charlie inactive-user).
///   GROUP_TEST_B_INACTIVE — inactive group; 1 membership (dave active-user).
///
/// Test coverage:
///   I1 — happy path: GROUP_TEST_A pool resolution yields alice + bob (charlie filtered by IsActive=false).
///   I2 — inactive group: GROUP_TEST_B_INACTIVE yields no users (group.IsActive=false filtered).
///   I3 — inactive user: charlie is in GROUP_TEST_A but ApplicationUser.IsActive=false → excluded.
///
/// Idempotent: checks by PublicId (group) and email (users) before inserting.
/// </summary>
public sealed partial class E2ETestDataSeeder
{
    // ── Fixed GUIDs for RT2-15 Phase 02 test data ─────────────────────────────────────────────

    /// <summary>Active group used in I1/I3 resolution tests.</summary>
    public static readonly Guid GroupTestAPublicId =
        Guid.Parse("70000000-0000-0000-0000-000000000001");

    /// <summary>Inactive group used in I2 inactive-group filter test.</summary>
    public static readonly Guid GroupTestBInactivePublicId =
        Guid.Parse("70000000-0000-0000-0000-000000000002");

    // Pinned user IDs for group member lookup in integration tests.
    public static readonly Guid GroupUserAliceId =
        Guid.Parse("71000000-0000-0000-0000-000000000001");
    public static readonly Guid GroupUserBobId =
        Guid.Parse("71000000-0000-0000-0000-000000000002");
    public static readonly Guid GroupUserCharlieId =
        Guid.Parse("71000000-0000-0000-0000-000000000003");
    public static readonly Guid GroupUserDaveId =
        Guid.Parse("71000000-0000-0000-0000-000000000004");

    // ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task SeedUserGroupsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // ── 1. Seed group-specific test users ────────────────────────────────────────────────

        // alice@groups.dev.local — active; member of GROUP_TEST_A.
        await EnsureGroupUserAsync(userManager, "alice@groups.dev.local", "Alice GroupTest",
            GroupUserAliceId, isActive: true);

        // bob@groups.dev.local — active; member of GROUP_TEST_A.
        await EnsureGroupUserAsync(userManager, "bob@groups.dev.local", "Bob GroupTest",
            GroupUserBobId, isActive: true);

        // charlie@groups.dev.local — IsActive=false; member of GROUP_TEST_A.
        // CRITICAL: must be filtered out from resolver results (I3 test).
        await EnsureGroupUserAsync(userManager, "charlie@groups.dev.local", "Charlie GroupTest",
            GroupUserCharlieId, isActive: false);

        // dave@groups.dev.local — active; member of GROUP_TEST_B_INACTIVE only.
        // Must be filtered (group inactive — I2 test).
        await EnsureGroupUserAsync(userManager, "dave@groups.dev.local", "Dave GroupTest",
            GroupUserDaveId, isActive: true);

        // ── 2. Seed groups (idempotency: check by PublicId) ───────────────────────────────────

        if (!await db.UserGroups.AnyAsync(g => g.PublicId == GroupTestAPublicId, ct))
        {
            var pinnedGen = new PinnedGuidGenerator(GroupTestAPublicId);
            var groupA = UserGroup.Create(
                pinnedGen,
                code: "GROUP_TEST_A",
                name: "Group Test A (Active)",
                description: "RT2-15 Phase 02 integration test group — active.",
                tenantId: DevTenantId,
                actorId: SystemActorId);
            // groupA.IsActive is true by default in factory — no Deactivate needed.
            db.UserGroups.Add(groupA);
            await db.SaveChangesAsync(ct); // groupA.Id assigned

            // ── 3. Seed memberships for GROUP_TEST_A ─────────────────────────────────────────

            var aliceId = await ResolveUserIdAsync(userManager, "alice@groups.dev.local", GroupUserAliceId);
            var bobId = await ResolveUserIdAsync(userManager, "bob@groups.dev.local", GroupUserBobId);
            var charlieId = await ResolveUserIdAsync(userManager, "charlie@groups.dev.local", GroupUserCharlieId);

            var memberships = new List<UserGroupMembership>();

            if (aliceId != Guid.Empty)
                memberships.Add(new UserGroupMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = aliceId,
                    GroupId = groupA.Id,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = SystemActorId,
                });

            if (bobId != Guid.Empty)
                memberships.Add(new UserGroupMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = bobId,
                    GroupId = groupA.Id,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = SystemActorId,
                });

            if (charlieId != Guid.Empty)
                memberships.Add(new UserGroupMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = charlieId,
                    GroupId = groupA.Id,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = SystemActorId,
                });

            db.UserGroupMemberships.AddRange(memberships);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.UserGroups.AnyAsync(g => g.PublicId == GroupTestBInactivePublicId, ct))
        {
            var pinnedGen = new PinnedGuidGenerator(GroupTestBInactivePublicId);
            var groupB = UserGroup.Create(
                pinnedGen,
                code: "GROUP_TEST_B_INACTIVE",
                name: "Group Test B (Inactive)",
                description: "RT2-15 Phase 02 integration test group — inactive (I2 filter test).",
                tenantId: DevTenantId,
                actorId: SystemActorId);
            groupB.Deactivate(SystemActorId);
            db.UserGroups.Add(groupB);
            await db.SaveChangesAsync(ct); // groupB.Id assigned

            // ── 4. Seed membership for GROUP_TEST_B_INACTIVE (dave) ───────────────────────────

            var daveId = await ResolveUserIdAsync(userManager, "dave@groups.dev.local", GroupUserDaveId);
            if (daveId != Guid.Empty)
            {
                db.UserGroupMemberships.Add(new UserGroupMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = daveId,
                    GroupId = groupB.Id,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = SystemActorId,
                });
                await db.SaveChangesAsync(ct);
            }
        }
    }

    /// <summary>
    /// Creates a group test user with a pinned Id and controlled IsActive state.
    /// IsActive is set directly after creation (ApplicationUser property, not lockout).
    /// Idempotent: skips if email or pinned Id already exists.
    /// </summary>
    private static async Task EnsureGroupUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        Guid pinnedId,
        bool isActive)
    {
        // Idempotent: skip if already exists by email or by pinned Id.
        if (await userManager.FindByEmailAsync(email) is not null)
            return;
        if (await userManager.FindByIdAsync(pinnedId.ToString()) is not null)
            return;

        var user = new ApplicationUser
        {
            Id = pinnedId,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = isActive,
            TenantId = DevTenantId,
        };

        var result = await userManager.CreateAsync(user, "DevGroup@12345");
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            Console.WriteLine($"[E2ETestDataSeeder.UserGroups] CreateUser '{email}' failed: {errors}");
        }
    }

    /// <summary>
    /// Resolves a user's Guid Id by email. Falls back to the pinned Id if email lookup fails.
    /// Returns Guid.Empty if neither lookup succeeds (membership row is skipped).
    /// </summary>
    private static async Task<Guid> ResolveUserIdAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        Guid pinnedFallback)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) return user.Id;

        user = await userManager.FindByIdAsync(pinnedFallback.ToString());
        return user?.Id ?? Guid.Empty;
    }
}
