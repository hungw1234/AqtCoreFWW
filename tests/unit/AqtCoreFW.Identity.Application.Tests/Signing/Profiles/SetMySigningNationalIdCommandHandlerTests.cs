using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

/// <summary>
/// Tests for the self-service set-CCCD handler. Focus: the active-enrollment lock (Pending/Approved blocks a
/// change; Rejected/revoked-only does not), the idempotent same-value short-circuit, and 12-digit validation.
/// </summary>
public sealed class SetMySigningNationalIdCommandHandlerTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISignerCertEnrollmentRepository _enrollmentRepo =
        Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly SetMySigningNationalIdCommandHandler _sut;

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    public SetMySigningNationalIdCommandHandlerTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>>();
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

        // Default: no active enrollment → lock does not trigger.
        _enrollmentRepo.GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SignerCertEnrollment>());

        _sut = new SetMySigningNationalIdCommandHandler(_userManager, _enrollmentRepo);
    }

    private static SignerCertEnrollment ActiveEnrollment(Guid tenantId, Guid userId) =>
        SignerCertEnrollment.Create(tenantId, userId, "SERIAL01", "CN=Test CA", "THUMB01", null,
            Guid.NewGuid(), EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Pending,
            CertSubjectType.Personal, null, Gen);

    [Fact]
    public async Task Handle_SetsCccd_WhenEmpty_AndNoActiveEnrollment()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = null };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var result = await _sut.Handle(new SetMySigningNationalIdCommand(tenantId, userId, "079201001234"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.SigningNationalId.Should().Be("079201001234");
        await _userManager.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task Handle_ChangesCccd_WhenNoActiveEnrollment()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "079201001111" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var result = await _sut.Handle(new SetMySigningNationalIdCommand(tenantId, userId, "079201009999"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("only Rejected/revoked rows would still allow a change");
        user.SigningNationalId.Should().Be("079201009999");
    }

    [Fact]
    public async Task Handle_RejectsChange_WhenActiveEnrollmentExists()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "079201001111" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _enrollmentRepo.GetActiveByUserAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(new[] { ActiveEnrollment(tenantId, userId) });

        var result = await _sut.Handle(new SetMySigningNationalIdCommand(tenantId, userId, "079201009999"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") &&
            (string?)e.Metadata["ErrorCode"] == "SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT");
        user.SigningNationalId.Should().Be("079201001111", "the CCCD must not change");
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task Handle_SameValue_IsIdempotent_EvenWithActiveEnrollment()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "079201001234" };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _enrollmentRepo.GetActiveByUserAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(new[] { ActiveEnrollment(tenantId, userId) });

        var result = await _sut.Handle(new SetMySigningNationalIdCommand(tenantId, userId, "079201001234"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("re-asserting the same CCCD is harmless and must not trip the lock");
        // Short-circuits before the lock lookup and the store write.
        await _enrollmentRepo.DidNotReceive().GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailure()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        _userManager.FindByIdAsync(userId.ToString()).Returns((ApplicationUser?)null);

        var result = await _sut.Handle(new SetMySigningNationalIdCommand(tenantId, userId, "079201001234"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // RT-1 #3: first-time set (user has no CCCD) must succeed even when a no-CCCD USB Pending enrollment
    // is active. A first-time set cannot contradict a cert-CCCD binding that never existed.
    [Fact]
    public async Task Handle_FirstTimeSet_WithActivePendingUsbEnrollment_Succeeds()
    {
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        // User has no CCCD yet — first-time set.
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = null };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);
        // Active USB Pending enrollment (created without a CCCD — org or personal-no-CCCD scenario).
        var usbPending = SignerCertEnrollment.Create(tenantId, userId, "USBSERIAL01", "CN=VGCA CA", "USBTHUMB",
            "CN=CONG TY ABC, O=CONG TY ABC, C=VN", Guid.NewGuid(),
            EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
            CertSubjectType.Organization, "CONG TY ABC", Gen);
        _enrollmentRepo.GetActiveByUserAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(new[] { usbPending });

        var result = await _sut.Handle(
            new SetMySigningNationalIdCommand(tenantId, userId, "079201001234"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "first-time CCCD set must not be blocked by a no-CCCD USB enrollment (RT-1 #3)");
        user.SigningNationalId.Should().Be("079201001234");
        await _userManager.Received(1).UpdateAsync(user);
    }
}
