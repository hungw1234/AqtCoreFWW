using AqtCoreFW.Identity.Application.Commands.UpdateUser;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateUserCommandHandler. Focus: the full mutable field set is round-tripped onto
/// the user — in particular PrimaryOrgUnitId, which the handler previously omitted while the
/// command carried OrgUnitId, so editing a user's org unit via PUT silently no-op'd — plus the
/// tenant-ownership guard on that now-reachable OrgUnitId assignment.
/// </summary>
public sealed class UpdateUserCommandHandlerTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOrgUnitModuleClient _orgUnitClient = Substitute.For<IOrgUnitModuleClient>();
    // H5 guard dependency: default stub returns false (no enrollment) so existing tests are unaffected.
    private readonly ISignerCertEnrollmentRepository _enrollmentRepo =
        Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly UpdateUserCommandHandler _sut;

    public UpdateUserCommandHandlerTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>,
                                   IUserPasswordStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        // Default: no enrollment → guard does not block
        _enrollmentRepo.HasAnyEnrollmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _sut = new UpdateUserCommandHandler(_userManager, _orgUnitClient, _enrollmentRepo);
    }

    /// <summary>Stub the cross-module lookup so the given OrgUnit resolves as in-tenant.</summary>
    private void OrgUnitResolves(Guid orgUnitId) =>
        _orgUnitClient.GetOrgUnitsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, OrgUnitInfoDto> { [orgUnitId] = new() { Id = orgUnitId, Name = "OU" } });

    [Fact]
    public async Task Handle_AssignsOrgUnitAndAllMutableFields()
    {
        var userId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, TenantId = Guid.NewGuid() };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);
        OrgUnitResolves(orgUnitId);

        var cmd = new UpdateUserCommand(userId, "Nguyen Van A", "DEPT-01", orgUnitId, Guid.NewGuid(), "079201001234");
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PrimaryOrgUnitId.Should().Be(orgUnitId);   // regression guard: was never assigned
        user.FullName.Should().Be("Nguyen Van A");
        user.DepartmentCode.Should().Be("DEPT-01");
        user.SigningNationalId.Should().Be("079201001234");
    }

    [Fact]
    public async Task Handle_OrgUnitLookupScopedToUsersTenant()
    {
        // The existence check must be scoped to the target user's tenant, not an ambient/global scope.
        var userId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, TenantId = tenantId };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);
        OrgUnitResolves(orgUnitId);

        var cmd = new UpdateUserCommand(userId, "Name", null, orgUnitId, Guid.NewGuid());
        await _sut.Handle(cmd, CancellationToken.None);

        await _orgUnitClient.Received(1).GetOrgUnitsByIdsAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == orgUnitId),
            tenantId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossTenantOrNonexistentOrgUnit_ReturnsFailureAndDoesNotPersist()
    {
        // Lookup omits cross-tenant / missing ids → empty result → reject before any mutation.
        var userId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, TenantId = Guid.NewGuid() };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _orgUnitClient.GetOrgUnitsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, OrgUnitInfoDto>());

        var cmd = new UpdateUserCommand(userId, "Name", null, orgUnitId, Guid.NewGuid());
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        user.PrimaryOrgUnitId.Should().BeNull();                 // not mutated
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task Handle_NullOrgUnit_ClearsAssignmentAndSkipsLookup()
    {
        // Full-replace PUT semantics: an omitted/null OrgUnitId clears the stored assignment
        // and must NOT call the cross-module lookup (nothing to validate).
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, PrimaryOrgUnitId = Guid.NewGuid() };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var cmd = new UpdateUserCommand(userId, "Name", null, null, Guid.NewGuid());
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PrimaryOrgUnitId.Should().BeNull();
        await _orgUnitClient.DidNotReceive().GetOrgUnitsByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        _userManager.FindByIdAsync(userId.ToString()).Returns((ApplicationUser?)null);

        var cmd = new UpdateUserCommand(userId, "Name", null, Guid.NewGuid(), Guid.NewGuid());
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ── H5 guard tests ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// H5 positive path: user has an enrollment (any status) and the command changes
    /// SigningNationalId → guard must reject with SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT.
    ///
    /// Red-before-green proof: temporarily removed the H5 if-block from the handler → this test
    /// turned RED (result.IsSuccess = true). Re-instated → GREEN. See phase-01 report.
    /// </summary>
    [Fact]
    public async Task Handle_H5_EnrolledUser_SigningNationalIdChange_ReturnsBadRequest()
    {
        var userId   = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id              = userId,
            TenantId        = tenantId,
            SigningNationalId = "079201001111",
        };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        // Simulate: user already has an enrollment (any status — could be Pending/Rejected/Approved)
        _enrollmentRepo.HasAnyEnrollmentAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Command changes the SigningNationalId to a different value
        var cmd = new UpdateUserCommand(userId, "Name", null, null, Guid.NewGuid(),
            SigningNationalId: "079201009999");
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue("enrolled user must not change their signing CCCD via PUT");
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") &&
            (string?)e.Metadata["ErrorCode"] == "SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT",
            "the guard must emit SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT specifically");
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    /// <summary>
    /// H5 pass-through: enrolled user, but SigningNationalId is UNCHANGED in the command
    /// → guard must NOT block (admin editing FullName/DeptCode while leaving CCCD the same).
    /// </summary>
    [Fact]
    public async Task Handle_H5_EnrolledUser_SigningNationalIdUnchanged_DoesNotBlock()
    {
        var userId    = Guid.NewGuid();
        var tenantId  = Guid.NewGuid();
        const string existingCccd = "079201001111";
        var user = new ApplicationUser
        {
            Id               = userId,
            TenantId         = tenantId,
            SigningNationalId = existingCccd,
        };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);
        // Enrollment exists — but CCCD is not being changed
        _enrollmentRepo.HasAnyEnrollmentAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Command keeps the same SigningNationalId value
        var cmd = new UpdateUserCommand(userId, "Updated Name", null, null, Guid.NewGuid(),
            SigningNationalId: existingCccd);
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "CCCD unchanged → guard does not trigger even with enrollment");
        // Guard MUST NOT call HasAnyEnrollmentAsync when value is unchanged (condition short-circuits)
        await _enrollmentRepo.DidNotReceive().HasAnyEnrollmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// H5 skip branch: user.TenantId is null (system/global user) → guard is bypassed entirely.
    /// System users have no cert enrollment concept; calling HasAnyEnrollmentAsync with null tenant
    /// would be a programming error.
    /// </summary>
    [Fact]
    public async Task Handle_H5_NullTenantUser_SigningNationalIdChange_Allowed()
    {
        var userId = Guid.NewGuid();
        // TenantId = null → global/system user
        var user = new ApplicationUser
        {
            Id               = userId,
            TenantId         = null,
            SigningNationalId = "OLD",
        };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var cmd = new UpdateUserCommand(userId, "Name", null, null, Guid.NewGuid(),
            SigningNationalId: "NEW");
        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("null-tenant users bypass the H5 guard");
        // Guard must NOT call the enrollment repository for null-tenant users
        await _enrollmentRepo.DidNotReceive().HasAnyEnrollmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
