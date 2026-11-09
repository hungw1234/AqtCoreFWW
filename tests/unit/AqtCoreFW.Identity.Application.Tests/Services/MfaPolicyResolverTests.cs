using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Services;

/// <summary>
/// TDD-first tests for MfaPolicyResolver.
/// Resolution rule: mandatory beats optional; among mandatories, shortest GraceDays wins.
/// No real DB — mocked IMfaPolicyRepository + UserManager.
/// </summary>
public sealed class MfaPolicyResolverTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaPolicyRepository _repo;
    private readonly MfaPolicyResolver _sut;

    private static readonly Guid SystemAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GovOfficerId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    // Fixed-Guid generator for deterministic test policies. PublicId value is irrelevant to resolver tests.
    private static readonly IGuidGenerator FixedGen = new FixedGuidGenerator(Guid.Parse("11111111-1111-7111-8111-111111111111"));

    // Seed policies matching plan §Architecture/Schema
    private static readonly MfaPolicy PolicySystemAdmin = MfaPolicy.Create(
        FixedGen, "SystemAdmin", isMandatory: true, graceDays: 0, MfaFactor.Totp, SystemAdminId);

    private static readonly MfaPolicy PolicyAdmin = MfaPolicy.Create(
        FixedGen, "Admin", isMandatory: true, graceDays: 7, MfaFactor.Totp, SystemAdminId);

    private static readonly MfaPolicy PolicyGovOfficer = MfaPolicy.Create(
        FixedGen, "GovOfficer", isMandatory: false, graceDays: 7, MfaFactor.Totp | MfaFactor.EmailOtp, SystemAdminId);

    public MfaPolicyResolverTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser>>();

        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _repo = Substitute.For<IMfaPolicyRepository>();
        _sut = new MfaPolicyResolver(_userManager, _repo);
    }

    private ApplicationUser MakeUser(Guid id) => new() { Id = id };

    // ── No roles / no policy ─────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserNotFound_ReturnsNull()
    {
        _userManager.FindByIdAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var result = await _sut.ResolveAsync(Guid.NewGuid());

        result.Should().BeNull("missing user has no applicable policy");
    }

    [Fact]
    public async Task ResolveAsync_UserInNoRoles_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(Array.Empty<string>());
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MfaPolicy>());

        var result = await _sut.ResolveAsync(userId);

        result.Should().BeNull("no roles → no policy");
    }

    [Fact]
    public async Task ResolveAsync_RolesExistButNoPolicyRows_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "SomeUnmappedRole" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MfaPolicy>());

        var result = await _sut.ResolveAsync(userId);

        result.Should().BeNull("no matching policy rows → null");
    }

    // ── Single role ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserInSystemAdmin_ReturnsSystemAdminPolicy()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "SystemAdmin" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PolicySystemAdmin });

        var result = await _sut.ResolveAsync(userId);

        result.Should().NotBeNull();
        result!.Role.Should().Be("SystemAdmin");
        result.IsMandatory.Should().BeTrue();
        result.GraceDays.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_UserInGovOfficerOnly_ReturnsGovOfficerPolicy()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "GovOfficer" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PolicyGovOfficer });

        var result = await _sut.ResolveAsync(userId);

        result.Should().NotBeNull();
        result!.Role.Should().Be("GovOfficer");
        result.IsMandatory.Should().BeFalse("GovOfficer policy is optional");
    }

    // ── Multi-role resolution ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserInAdminAndGovOfficer_ReturnsMandatoryAdmin()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "Admin", "GovOfficer" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PolicyAdmin, PolicyGovOfficer });

        var result = await _sut.ResolveAsync(userId);

        result.Should().NotBeNull();
        result!.Role.Should().Be("Admin", "mandatory beats optional");
        result.IsMandatory.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_UserInSystemAdminAndAdmin_ReturnsSystemAdminWithZeroGrace()
    {
        // Both mandatory — shortest GraceDays wins (SystemAdmin=0 beats Admin=7)
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "SystemAdmin", "Admin" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PolicySystemAdmin, PolicyAdmin });

        var result = await _sut.ResolveAsync(userId);

        result.Should().NotBeNull();
        result!.Role.Should().Be("SystemAdmin", "among mandatories, shortest GraceDays wins");
        result.GraceDays.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_AllThreeRoles_ReturnsMostRestrictive()
    {
        // SystemAdmin (mandatory, 0d) + Admin (mandatory, 7d) + GovOfficer (optional, 7d)
        // → SystemAdmin wins
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new[] { "SystemAdmin", "Admin", "GovOfficer" });
        _repo.GetByRoleNamesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PolicySystemAdmin, PolicyAdmin, PolicyGovOfficer });

        var result = await _sut.ResolveAsync(userId);

        result.Should().NotBeNull();
        result!.Role.Should().Be("SystemAdmin");
        result.GraceDays.Should().Be(0);
    }
}

/// <summary>Deterministic IGuidGenerator stub — always returns the same fixed Guid.</summary>
file sealed class FixedGuidGenerator(Guid id) : IGuidGenerator
{
    public Guid NewIdMonotonic() => id;
    public Guid NewIdRandom() => id;
}
