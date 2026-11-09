using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.AuthServer.Tests.Services;

/// <summary>
/// QĐ 742 1.6.b — pure-predicate tests for MfaEnrolmentEvaluator.
/// Compliance contract: returns true ⇔ TotpEnrolledAt null ∧ mandatory policy ∧ grace expired.
/// Feature-flag gating lives in AuthorizationController.CreateIdentity (caller), NOT here —
/// keeps ROPC's hard-block path independent of the FE-redirect toggle.
/// </summary>
public sealed class MfaEnrolmentEvaluatorTests
{
    private readonly IMfaPolicyResolver _resolver = Substitute.For<IMfaPolicyResolver>();
    private readonly MfaEnrolmentEvaluator _sut;
    private static readonly Guid SystemUser = Guid.NewGuid();
    // Fixed generator — PublicId is irrelevant to enrolment evaluator tests.
    private static readonly IGuidGenerator FixedGen = new FixedGuidGenerator(Guid.Parse("22222222-2222-7222-8222-222222222222"));

    public MfaEnrolmentEvaluatorTests()
    {
        _sut = new MfaEnrolmentEvaluator(_resolver);
    }

    [Fact]
    public async Task AlreadyEnrolled_ReturnsFalse_AndSkipsResolverCall()
    {
        var user = NewUser(totpEnrolled: true);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeFalse();
        await _resolver.DidNotReceive().ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotEnrolled_NoPolicyResolved_ReturnsFalse()
    {
        var user = NewUser(totpEnrolled: false);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns((MfaPolicy?)null);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NotEnrolled_OptionalPolicy_ReturnsFalse()
    {
        var user = NewUser(totpEnrolled: false);
        var policy = MfaPolicy.Create(FixedGen, "GovOfficer", isMandatory: false, graceDays: 7, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NotEnrolled_MandatoryPolicy_GraceActive_ReturnsFalse()
    {
        // CreatedAt = now-1d, GraceDays=7 → deadline = now+6d (still in grace)
        var user = NewUser(totpEnrolled: false, createdDaysAgo: 1);
        var policy = MfaPolicy.Create(FixedGen, "Admin", isMandatory: true, graceDays: 7, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NotEnrolled_MandatoryPolicy_GraceExpired_ReturnsTrue()
    {
        // CreatedAt = now-10d, GraceDays=7 → deadline = now-3d (expired)
        var user = NewUser(totpEnrolled: false, createdDaysAgo: 10);
        var policy = MfaPolicy.Create(FixedGen, "Admin", isMandatory: true, graceDays: 7, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task NotEnrolled_MandatoryPolicy_ZeroGraceSystemAdmin_ReturnsTrueOnFirstLogin()
    {
        // SystemAdmin row: GraceDays=0 → deadline = now (always expired immediately)
        var user = NewUser(totpEnrolled: false, createdDaysAgo: 0); // just created, but grace=0
        // Pretend creation was a few seconds ago to clear DateTime.UtcNow > deadline
        user.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-5);
        var policy = MfaPolicy.Create(FixedGen, "SystemAdmin", isMandatory: true, graceDays: 0, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task NotEnrolled_ExplicitMfaGraceUntil_OverridesCreatedAt()
    {
        // Even with very recent CreatedAt, an explicit MfaGraceUntil in the past must drive deadline.
        var user = NewUser(totpEnrolled: false, createdDaysAgo: 0);
        user.MfaGraceUntil = DateTime.UtcNow.AddHours(-1); // expired 1h ago
        var policy = MfaPolicy.Create(FixedGen, "Admin", isMandatory: true, graceDays: 30, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task NotEnrolled_ExplicitMfaGraceUntil_FuturePostpones()
    {
        // Even with very old CreatedAt, an explicit MfaGraceUntil in the future blocks expiry.
        var user = NewUser(totpEnrolled: false, createdDaysAgo: 365);
        user.MfaGraceUntil = DateTime.UtcNow.AddDays(3); // 3 more days
        var policy = MfaPolicy.Create(FixedGen, "Admin", isMandatory: true, graceDays: 7, MfaFactor.Totp, SystemUser);
        _resolver.ResolveAsync(user.Id, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await _sut.RequiresEnrolmentAsync(user);

        result.Should().BeFalse();
    }

    private static ApplicationUser NewUser(bool totpEnrolled, int createdDaysAgo = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserName = "test@example.com",
        Email = "test@example.com",
        FullName = "Test User",
        CreatedAtUtc = DateTime.UtcNow.AddDays(-createdDaysAgo),
        TotpEnrolledAt = totpEnrolled ? DateTime.UtcNow.AddDays(-1) : null,
    };
}

/// <summary>Deterministic IGuidGenerator for test-local use within this file.</summary>
file sealed class FixedGuidGenerator(Guid id) : IGuidGenerator
{
    public Guid NewIdMonotonic() => id;
    public Guid NewIdRandom() => id;
}
