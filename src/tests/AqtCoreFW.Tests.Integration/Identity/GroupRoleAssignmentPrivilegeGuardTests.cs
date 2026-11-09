using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Integration tests for privilege-escalation guard on AssignRoleToGroupCommandHandler.
///
/// Assigning a role to a group makes every group member inherit that role. System-typed roles
/// and roles named Admin/SystemAdmin are privileged and may only be assigned by SystemAdmin actors.
/// This test covers the guard applied post-role-lookup (lines 42–50 of handler).
/// </summary>
[Collection("Integration")]
public class GroupRoleAssignmentPrivilegeGuardTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    /// <summary>
    /// Non-SystemAdmin actor assigning a System-typed role to a group → ForbiddenError (422).
    /// No GroupRoleAssignment row is created.
    /// </summary>
    [Fact]
    public async Task AssignRoleToGroup_NonSystemAdminAssignsSystemRole_ReturnsForbidden()
    {
        var tenant = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create group, system role, and non-admin actor
        var group = UserGroup.Create(
            new GuidGeneratorForTest(),
            "TEST_SYS_GUARD",
            "Test System Guard Group",
            null,
            tenant,
            Guid.NewGuid());
        var systemRole = NewRole("Test System Role", "TEST_SYS", tenantId: null, normalizer, roleType: RoleType.System);
        var nonAdminActor = NewUser(tenant, normalizer);

        dbCtx.UserGroups.Add(group);
        dbCtx.Roles.Add(systemRole);
        dbCtx.Users.Add(nonAdminActor);
        await dbCtx.SaveChangesAsync();

        try
        {
            var result = await mediator.Send(
                new AssignRoleToGroupCommand(group.PublicId, systemRole.Id, nonAdminActor.Id));

            result.IsSuccess.Should().BeFalse("non-SystemAdmin must not assign System roles to groups");
            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("Chỉ SystemAdmin");

            // Verify no assignment was created
            var assignment = await dbCtx.GroupRoleAssignments
                .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.RoleId == systemRole.Id);
            assignment.Should().BeNull("guard must prevent row creation on Forbidden");
        }
        finally
        {
            await dbCtx.GroupRoleAssignments.Where(a => a.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == nonAdminActor.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.Id == systemRole.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Non-SystemAdmin actor assigning a Business-typed role named "SystemAdmin" to a group
    /// (seeder mistype scenario) → ForbiddenError.
    /// Guard checks both RoleType.System AND role names Admin/SystemAdmin, so name alone is sufficient.
    /// </summary>
    [Fact]
    public async Task AssignRoleToGroup_NonSystemAdminAssignsSystemAdminRoleByName_ReturnsForbidden()
    {
        var tenant = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create group and a Business role named "SystemAdmin" (seeder edge case)
        var group = UserGroup.Create(
            new GuidGeneratorForTest(),
            "TEST_NAME_GUARD",
            "Test Name Guard Group",
            null,
            tenant,
            Guid.NewGuid());
        var fakeSystemAdminRole = NewRole(
            Roles.SystemAdmin,  // Name = "SystemAdmin"
            "FAKE_SYS_ADMIN",
            tenantId: tenant,   // Business tenant-scoped role, not System
            normalizer,
            roleType: RoleType.Business);
        var nonAdminActor = NewUser(tenant, normalizer);

        dbCtx.UserGroups.Add(group);
        dbCtx.Roles.Add(fakeSystemAdminRole);
        dbCtx.Users.Add(nonAdminActor);
        await dbCtx.SaveChangesAsync();

        try
        {
            var result = await mediator.Send(
                new AssignRoleToGroupCommand(group.PublicId, fakeSystemAdminRole.Id, nonAdminActor.Id));

            result.IsSuccess.Should().BeFalse(
                "guard must reject privileged role names even if RoleType is Business");
            result.Errors[0].Message.Should().Contain("Chỉ SystemAdmin");

            // Verify no assignment row created
            var assignment = await dbCtx.GroupRoleAssignments
                .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.RoleId == fakeSystemAdminRole.Id);
            assignment.Should().BeNull();
        }
        finally
        {
            await dbCtx.GroupRoleAssignments.Where(a => a.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == nonAdminActor.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.Id == fakeSystemAdminRole.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// SystemAdmin actor assigning a System-typed role to a group → Success.
    /// Assignment row is created; guard is bypassed for SystemAdmin actors.
    /// </summary>
    [Fact]
    public async Task AssignRoleToGroup_SystemAdminAssignsSystemRole_Succeeds()
    {
        var tenant = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create group, system role, and SystemAdmin actor
        var group = UserGroup.Create(
            new GuidGeneratorForTest(),
            "TEST_ADMIN_ALLOW",
            "Test Admin Allow Group",
            null,
            tenant,
            Guid.NewGuid());
        var systemRole = NewRole("Test System Role", "TEST_SYS_2", tenantId: null, normalizer, roleType: RoleType.System);
        var systemAdminRole = NewRole(Roles.SystemAdmin, "SYS_ADMIN", tenantId: null, normalizer, roleType: RoleType.System);
        var systemAdminActor = NewUser(tenant, normalizer);

        dbCtx.UserGroups.Add(group);
        dbCtx.Roles.AddRange(systemRole, systemAdminRole);
        dbCtx.Users.Add(systemAdminActor);
        await dbCtx.SaveChangesAsync();

        // Assign SystemAdmin role to actor
        await userManager.AddToRoleAsync(systemAdminActor, Roles.SystemAdmin);

        try
        {
            var result = await mediator.Send(
                new AssignRoleToGroupCommand(group.PublicId, systemRole.Id, systemAdminActor.Id));

            result.IsSuccess.Should().BeTrue(
                "SystemAdmin actor must be able to assign System roles to groups");

            // Verify assignment row was created
            var assignment = await dbCtx.GroupRoleAssignments
                .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.RoleId == systemRole.Id);
            assignment.Should().NotBeNull("assignment must exist");
            assignment!.AssignedBy.Should().Be(systemAdminActor.Id);
        }
        finally
        {
            await dbCtx.GroupRoleAssignments.Where(a => a.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserRoles.Where(ur => ur.UserId == systemAdminActor.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == systemAdminActor.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.Id == systemRole.Id || r.Id == systemAdminRole.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Non-SystemAdmin actor assigning an ordinary Business role to a group → Success.
    /// Guard does not block regular business roles; only System-typed and privileged-named roles.
    /// </summary>
    [Fact]
    public async Task AssignRoleToGroup_NonSystemAdminAssignsBusinessRole_Succeeds()
    {
        var tenant = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var mediator = sp.GetRequiredService<IMediator>();

        // Create group, ordinary business role, and non-admin actor
        var group = UserGroup.Create(
            new GuidGeneratorForTest(),
            "TEST_BUSINESS_ALLOW",
            "Test Business Allow Group",
            null,
            tenant,
            Guid.NewGuid());
        var businessRole = NewRole(
            "Officer",
            "OFFICER",
            tenantId: tenant,
            normalizer,
            roleType: RoleType.Business);
        var nonAdminActor = NewUser(tenant, normalizer);

        dbCtx.UserGroups.Add(group);
        dbCtx.Roles.Add(businessRole);
        dbCtx.Users.Add(nonAdminActor);
        await dbCtx.SaveChangesAsync();

        try
        {
            var result = await mediator.Send(
                new AssignRoleToGroupCommand(group.PublicId, businessRole.Id, nonAdminActor.Id));

            result.IsSuccess.Should().BeTrue(
                "non-SystemAdmin must be able to assign ordinary business roles to groups");

            // Verify assignment row was created
            var assignment = await dbCtx.GroupRoleAssignments
                .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.RoleId == businessRole.Id);
            assignment.Should().NotBeNull("assignment must exist");
            assignment!.AssignedBy.Should().Be(nonAdminActor.Id);
        }
        finally
        {
            await dbCtx.GroupRoleAssignments.Where(a => a.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroupMemberships.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync();
            await dbCtx.UserGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == nonAdminActor.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.Id == businessRole.Id).ExecuteDeleteAsync();
        }
    }

    private static ApplicationRole NewRole(
        string name,
        string code,
        Guid? tenantId,
        ILookupNormalizer normalizer,
        RoleType roleType = RoleType.Business)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizer.NormalizeName(name),
            Code = code,
            RoleType = roleType,
            IsActive = true,
            TenantId = tenantId,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };

    private static ApplicationUser NewUser(Guid tenantId, ILookupNormalizer normalizer)
    {
        var email = $"group-role-{Guid.NewGuid():N}@test.vn";
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email),
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email),
            FullName = "Group Role Test User",
            TenantId = tenantId,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
    }

    /// <summary>Minimal GuidGenerator stub for UserGroup.Create factory.</summary>
    private class GuidGeneratorForTest : AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.NewGuid();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
}
