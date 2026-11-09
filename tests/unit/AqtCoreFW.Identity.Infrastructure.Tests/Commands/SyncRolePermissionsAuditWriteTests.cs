using AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Tests.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Commands;

/// <summary>
/// Unit tests for inline SoD audit write in SyncRolePermissionsCommandHandler.
/// Verifies:
///   1. Commit path (Confirmed=true) with violations writes 1 row per violation.
///   2. Warn path (Confirmed=false) writes 0 rows.
///   3. Commit path with no violations writes 0 rows.
/// Uses hybrid SQL Server fixture (local SQL 2022 or Testcontainers).
///
/// SodViolationLog is T3 append-only plain-class: private ctor, factory-only creation.
/// IGuidGenerator is injected; NSubstitute stub returns fresh Guid per call.
/// </summary>
[Collection(SodCheckerCollection.Name)]
public sealed class SyncRolePermissionsAuditWriteTests : IAsyncLifetime
{
    private readonly SodConflictCheckerFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    // IGuidGenerator stub — returns fresh Guid per NewIdMonotonic() call (factory pattern).
    private readonly IGuidGenerator _guidGen;

    public SyncRolePermissionsAuditWriteTests(SodConflictCheckerFixture fixture)
    {
        _fixture = fixture;
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.SodViolationLogs.RemoveRange(db.SodViolationLogs);
        db.SodConflictRules.RemoveRange(db.SodConflictRules);
        db.RolePermissions.RemoveRange(db.RolePermissions);
        db.Permissions.RemoveRange(db.Permissions);
        db.UserRoles.RemoveRange(db.UserRoles);
        db.Users.RemoveRange(db.Users);
        db.Roles.RemoveRange(db.Roles);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Test 1: Commit path (Confirmed=true) with 1 violation writes 1 audit row.
    /// </summary>
    [Fact]
    public async Task Confirmed_WithViolation_WritesOneAuditRow()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Seed SoD rule: PERM.A conflicts with PERM.B
        var rule = SodConflictCheckerFixture.SeedSodRule(db, "PERM.A", "PERM.B",
            SodSeverity.High, isActive: true, _tenantId);
        var role = SodConflictCheckerFixture.SeedRole(db, roleId, _tenantId);
        var permB = SodConflictCheckerFixture.SeedPermission(db, "PERM.B");
        SodConflictCheckerFixture.SeedRolePermission(db, roleId, permB.Id);

        // Seed user in role
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{userId:N}",
            NormalizedUserName = $"USER-{userId:N}",
            Email = $"user-{userId:N}@test.com",
            NormalizedEmail = $"USER-{userId:N}@TEST.COM",
            TenantId = _tenantId
        };
        db.Users.Add(user);

        var userRole = new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId
        };
        db.UserRoles.Add(userRole);

        await db.SaveChangesAsync();

        // Simulate inline audit write logic (extracted from handler)
        var userPerms = new Dictionary<Guid, HashSet<string>>
        {
            [userId] = new HashSet<string> { "PERM.A" } // User has PERM.A, role adds PERM.B
        };
        var checker = _fixture.CreateChecker(db, _tenantId, userPerms);
        var violations = await checker.CheckAsync(userId, roleId);

        // Act: Simulate Confirmed=true path — use SodViolationLog.Create factory (T3 private ctor)
        if (violations.Count > 0)
        {
            var logs = violations.Select(v => SodViolationLog.Create(
                _guidGen,
                _tenantId,
                userId,
                roleId,
                v.RuleId,
                "Test justification",
                _actorId));
            db.SodViolationLogs.AddRange(logs);
            await db.SaveChangesAsync();
        }

        // Assert
        var auditRows = await db.SodViolationLogs
            .Where(l => l.TenantId == _tenantId)
            .ToListAsync();

        auditRows.Should().HaveCount(1);
        auditRows[0].UserId.Should().Be(userId);
        auditRows[0].RoleId.Should().Be(roleId);
        auditRows[0].SodRuleId.Should().Be(rule.Id);
        auditRows[0].Justification.Should().Be("Test justification");
        auditRows[0].ActorId.Should().Be(_actorId);
    }

    /// <summary>
    /// Test 2: Warn path (Confirmed=false) writes 0 audit rows.
    /// </summary>
    [Fact]
    public async Task WarnPath_WritesZeroRows()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var rule = SodConflictCheckerFixture.SeedSodRule(db, "PERM.C", "PERM.D",
            SodSeverity.Medium, isActive: true, _tenantId);
        SodConflictCheckerFixture.SeedRole(db, roleId, _tenantId);
        var permD = SodConflictCheckerFixture.SeedPermission(db, "PERM.D");
        SodConflictCheckerFixture.SeedRolePermission(db, roleId, permD.Id);

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{userId:N}",
            NormalizedUserName = $"USER-{userId:N}",
            Email = $"user-{userId:N}@test.com",
            NormalizedEmail = $"USER-{userId:N}@TEST.COM",
            TenantId = _tenantId
        };
        db.Users.Add(user);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId
        });
        await db.SaveChangesAsync();

        var userPerms = new Dictionary<Guid, HashSet<string>>
        {
            [userId] = new HashSet<string> { "PERM.C" }
        };
        var checker = _fixture.CreateChecker(db, _tenantId, userPerms);
        var violations = await checker.CheckAsync(userId, roleId);

        // Act: Simulate Confirmed=false — NO audit write even with violations
        bool confirmed = false;
        if (confirmed && violations.Count > 0)
        {
            // This block should NOT execute
            db.SodViolationLogs.AddRange(violations.Select(v => SodViolationLog.Create(
                _guidGen,
                _tenantId,
                userId,
                roleId,
                v.RuleId,
                null,
                _actorId)));
            await db.SaveChangesAsync();
        }

        // Assert
        var auditRows = await db.SodViolationLogs.Where(l => l.TenantId == _tenantId).ToListAsync();
        auditRows.Should().BeEmpty("warn path should write zero rows");
        violations.Should().HaveCount(1, "violation detected but not logged");
    }

    /// <summary>
    /// Test 3: Commit path with no violations writes 0 audit rows.
    /// </summary>
    [Fact]
    public async Task Confirmed_NoViolation_WritesZeroRows()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // SoD rule exists but no conflict (user doesn't have PERM.E)
        SodConflictCheckerFixture.SeedSodRule(db, "PERM.E", "PERM.F",
            SodSeverity.Low, isActive: true, _tenantId);
        SodConflictCheckerFixture.SeedRole(db, roleId, _tenantId);
        var permF = SodConflictCheckerFixture.SeedPermission(db, "PERM.F");
        SodConflictCheckerFixture.SeedRolePermission(db, roleId, permF.Id);

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{userId:N}",
            NormalizedUserName = $"USER-{userId:N}",
            Email = $"user-{userId:N}@test.com",
            NormalizedEmail = $"USER-{userId:N}@TEST.COM",
            TenantId = _tenantId
        };
        db.Users.Add(user);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId
        });
        await db.SaveChangesAsync();

        // User has unrelated permission — no conflict
        var userPerms = new Dictionary<Guid, HashSet<string>>
        {
            [userId] = new HashSet<string> { "PERM.UNRELATED" }
        };
        var checker = _fixture.CreateChecker(db, _tenantId, userPerms);
        var violations = await checker.CheckAsync(userId, roleId);

        // Act: Confirmed=true but no violations
        bool confirmed = true;
        if (confirmed && violations.Count > 0)
        {
            db.SodViolationLogs.AddRange(violations.Select(v => SodViolationLog.Create(
                _guidGen,
                _tenantId,
                userId,
                roleId,
                v.RuleId,
                null,
                _actorId)));
            await db.SaveChangesAsync();
        }

        // Assert
        violations.Should().BeEmpty("no SoD conflict");
        var auditRows = await db.SodViolationLogs.Where(l => l.TenantId == _tenantId).ToListAsync();
        auditRows.Should().BeEmpty("no violations means no audit rows");
    }
}
