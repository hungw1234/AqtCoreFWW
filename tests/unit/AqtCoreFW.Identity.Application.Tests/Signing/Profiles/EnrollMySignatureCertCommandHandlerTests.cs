using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

// Possession-proof crypto itself (raw RSA verify) is exercised end-to-end with real keys in the
// integration suite; these unit tests prove the handler's branching: nonce validation + anti-replay,
// CCCD re-assert, idempotency, pending-cap, the provider fork, and the auto-approve-leak guard.
public sealed class EnrollMySignatureCertCommandHandlerTests
{
    private const string Cccd = "012345678901";
    private const string Serial = "ABCD1234";
    private const string Issuer = "CN=Test CA";
    private static readonly string Subject = $"CN=Nguyen Van A, SERIALNUMBER=CCCD:{Cccd}, C=VN";

    private readonly ISignerEnrollmentNonceRepository _nonces = Substitute.For<ISignerEnrollmentNonceRepository>();
    private readonly ISignerCertEnrollmentRepository _enrollments = Substitute.For<ISignerCertEnrollmentRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly UserManager<ApplicationUser> _userManager = MakeUserManager();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private EnrollMySignatureCertCommandHandler Sut() =>
        new(_nonces, _enrollments, _mediator, _userManager, Gen);

    public EnrollMySignatureCertCommandHandlerTests()
    {
        // Default happy wiring: caller exists in-tenant with an established CCCD; no prior enrollments.
        var user = new ApplicationUser { Id = _callerId, TenantId = _tenantId, SigningNationalId = Cccd };
        _userManager.FindByIdAsync(_callerId.ToString()).Returns(user);
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SignerCertEnrollment>());
    }

    private static CertificateInfo MatchingCert() =>
        new(Serial, Subject, Issuer, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMB1", [1, 2, 3]);

    private SignerEnrollmentNonce ValidNonce()
    {
        var nonce = SignerEnrollmentNonce.Issue(Gen, _tenantId, _callerId, new byte[32]);
        _nonces.GetAsync(_tenantId, nonce.Id, Arg.Any<CancellationToken>()).Returns(nonce);
        return nonce;
    }

    private void StubVerifyValid(bool isValid, CertificateInfo? cert) =>
        _mediator.Send(Arg.Any<VerifySignatureCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new VerifySignatureResultDto(isValid, isValid ? null : "bad sig", cert, null)));

    private EnrollMySignatureCertCommand UsbCmd(Guid nonceId) =>
        new(_tenantId, _callerId, EnrollmentKind.UsbTokenVgca, nonceId,
            SignedNonceBase64: Convert.ToBase64String(new byte[] { 9, 9 }), CertBase64: Convert.ToBase64String([1, 2, 3]));

    // ── USB happy + auto-approve-leak teeth ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Usb_ValidPossession_CreatesPendingEnrollment()
    {
        var nonce = ValidNonce();
        StubVerifyValid(true, MatchingCert());
        SignerCertEnrollment? created = null;
        _enrollments.AddAsync(Arg.Do<SignerCertEnrollment>(e => created = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        created.Should().NotBeNull();
        created!.Kind.Should().Be(EnrollmentKind.UsbTokenVgca);
        created.ApprovalStatus.Should().Be(CertApprovalStatus.Pending,
            "self-service enrollment can never auto-approve — H2 teeth");
        created.SerialNumber.Should().Be(Serial);
        nonce.IsConsumed.Should().BeTrue("nonce is consumed on a successful enroll (single-use)");
        await _nonces.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Usb_InvalidPossession_Fails_NoEnrollment()
    {
        var nonce = ValidNonce();
        StubVerifyValid(false, null);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_POSSESSION_PROOF_FAILED");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
        nonce.IsConsumed.Should().BeTrue("nonce is consumed even on a failed attempt — anti-replay over UX");
    }

    // ── Remote (Mysign sign-on-nonce) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remote_ValidPossession_CreatesPendingEnrollment_KindRemote()
    {
        var nonce = ValidNonce();
        // Remote signs through SignPayloadCommand (tenant-provider, provider-agnostic) — that path resolves
        // the provider, signs on the caller's national id, and returns the cert. The possession-proof MUST
        // request SkipBindingGuard: proving possession is what CREATES the binding, so running the guard
        // (which needs an already-approved enrollment) would be circular. Stub a successful sync result.
        _mediator.Send(Arg.Any<SignPayloadCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SignPayloadResultDto(
                IsSuccess: true, IsPending: false, Signature: new byte[] { 7, 7 },
                TransactionId: null, FailureReason: null, SigningCertificate: MatchingCert())));
        SignerCertEnrollment? created = null;
        _enrollments.AddAsync(Arg.Do<SignerCertEnrollment>(e => created = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cmd = new EnrollMySignatureCertCommand(
            _tenantId, _callerId, EnrollmentKind.RemoteViettelMysign, nonce.Id, null, null);
        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        created!.Kind.Should().Be(EnrollmentKind.RemoteViettelMysign);
        created.ApprovalStatus.Should().Be(CertApprovalStatus.Pending);
        // The possession-proof sign must bypass the anti-impersonation binding guard (else a signer with a
        // prior enrollment could never enroll a second, not-yet-bound cert — the reported enrollment bug).
        await _mediator.Received().Send(
            Arg.Is<SignPayloadCommand>(c => c.SkipBindingGuard), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remote_PendingResult_Fails_NoEnrollment()
    {
        var nonce = ValidNonce();
        // Async/callback mode → IsSuccess=true but IsPending with no certificate yet → not a completed proof.
        _mediator.Send(Arg.Any<SignPayloadCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SignPayloadResultDto(
                IsSuccess: true, IsPending: true, Signature: null,
                TransactionId: "tx-1", FailureReason: null, SigningCertificate: null)));

        var cmd = new EnrollMySignatureCertCommand(
            _tenantId, _callerId, EnrollmentKind.RemoteViettelMysign, nonce.Id, null, null);
        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_POSSESSION_PROOF_FAILED");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    // ── Nonce validation + anti-replay ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nonce_Missing_Fails()
    {
        _nonces.GetAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SignerEnrollmentNonce?)null);

        var result = await Sut().Handle(UsbCmd(Guid.NewGuid()), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_NONCE_INVALID");
    }

    [Fact]
    public async Task Nonce_OfAnotherUser_Fails_AntiReplay()
    {
        var otherUserNonce = SignerEnrollmentNonce.Issue(Gen, _tenantId, Guid.NewGuid(), new byte[32]);
        _nonces.GetAsync(_tenantId, otherUserNonce.Id, Arg.Any<CancellationToken>()).Returns(otherUserNonce);

        var result = await Sut().Handle(UsbCmd(otherUserNonce.Id), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_NONCE_INVALID");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentConsume_OptimisticConflict_ReturnsCleanAlreadyUsed()
    {
        var nonce = ValidNonce();
        // A concurrent submit won the single-use race → the consume SaveChanges throws on RowVersion mismatch.
        _nonces.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DbUpdateConcurrencyException()));

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_NONCE_CONSUMED");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nonce_AlreadyConsumed_Fails()
    {
        var nonce = SignerEnrollmentNonce.Issue(Gen, _tenantId, _callerId, new byte[32]);
        nonce.Consume(DateTime.UtcNow);
        _nonces.GetAsync(_tenantId, nonce.Id, Arg.Any<CancellationToken>()).Returns(nonce);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_NONCE_CONSUMED");
    }

    // ── CCCD precondition + provider-scoped re-assert ──────────────────────────────────────────────

    // Remote: CCCD must be established before enroll (account key for Mysign).
    [Fact]
    public async Task Remote_CccdNotEstablished_Fails()
    {
        _userManager.FindByIdAsync(_callerId.ToString())
            .Returns(new ApplicationUser { Id = _callerId, TenantId = _tenantId, SigningNationalId = null });

        var result = await Sut().Handle(
            new EnrollMySignatureCertCommand(_tenantId, _callerId, EnrollmentKind.RemoteViettelMysign,
                Guid.NewGuid(), null, null),
            CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "SIGNING_NATIONAL_ID_NOT_ESTABLISHED");
    }

    // Remote: cert with no national-id RDN must fail (CCCD is the Mysign account key).
    [Fact]
    public async Task Remote_CertMissingCccd_Fails_NoNationalId()
    {
        var nonce = ValidNonce();
        var orgCert = new CertificateInfo(
            Serial, "CN=CONG TY ABC, O=CONG TY ABC, C=VN", Issuer,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMB2", [2, 3]);
        _mediator.Send(Arg.Any<SignPayloadCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SignPayloadResultDto(
                IsSuccess: true, IsPending: false, Signature: new byte[] { 7 },
                TransactionId: null, FailureReason: null, SigningCertificate: orgCert)));

        var cmd = new EnrollMySignatureCertCommand(
            _tenantId, _callerId, EnrollmentKind.RemoteViettelMysign, nonce.Id, null, null);
        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_CERT_NO_NATIONAL_ID");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    // Remote: cert CCCD ≠ user CCCD must fail (byte-for-byte old behaviour preserved).
    [Fact]
    public async Task Remote_CccdMismatch_Fails()
    {
        var nonce = ValidNonce();
        var wrongCert = new CertificateInfo(
            Serial, "CN=X, SERIALNUMBER=CCCD:999999999999, C=VN", Issuer,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMB3", [3, 4]);
        _mediator.Send(Arg.Any<SignPayloadCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SignPayloadResultDto(
                IsSuccess: true, IsPending: false, Signature: new byte[] { 7 },
                TransactionId: null, FailureReason: null, SigningCertificate: wrongCert)));

        var cmd = new EnrollMySignatureCertCommand(
            _tenantId, _callerId, EnrollmentKind.RemoteViettelMysign, nonce.Id, null, null);
        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_CERT_NATIONAL_ID_MISMATCH");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    // USB: Personal cert whose CCCD mismatches the user's → HARD REJECT (RT-1 #1 preserved).
    [Fact]
    public async Task Usb_PersonalCert_CccdMismatch_HardRejects()
    {
        var nonce = ValidNonce();
        var wrongCert = new CertificateInfo(
            Serial, "CN=X, SERIALNUMBER=CCCD:999999999999, C=VN", Issuer,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMB1", [1]);
        StubVerifyValid(true, wrongCert);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_CERT_NATIONAL_ID_MISMATCH");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    // USB: org cert (no CCCD in subject) → accepted; SubjectType=Organization persisted.
    [Fact]
    public async Task Usb_OrgCert_NoCccdInSubject_Succeeds_SubjectTypeOrganization()
    {
        var nonce = ValidNonce();
        var orgCert = new CertificateInfo(
            "ORGSERIAL", "CN=CONG TY ABC, O=CONG TY ABC, C=VN", Issuer,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMBORG", [5, 6]);
        StubVerifyValid(true, orgCert);
        SignerCertEnrollment? created = null;
        _enrollments.AddAsync(Arg.Do<SignerCertEnrollment>(e => created = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("org cert requires no CCCD");
        created.Should().NotBeNull();
        created!.ApprovalStatus.Should().Be(CertApprovalStatus.Pending);
        created.SubjectType.Should().Be(CertSubjectType.Organization);
    }

    // USB: Personal cert whose CCCD matches the user's → accepted; SubjectType=Personal persisted.
    [Fact]
    public async Task Usb_PersonalCert_MatchingCccd_Succeeds_SubjectTypePersonal()
    {
        var nonce = ValidNonce();
        StubVerifyValid(true, MatchingCert()); // subject has CCCD:012345678901, user has 012345678901
        SignerCertEnrollment? created = null;
        _enrollments.AddAsync(Arg.Do<SignerCertEnrollment>(e => created = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        created!.SubjectType.Should().Be(CertSubjectType.Personal);
    }

    // USB: user has NO SigningNationalId + org cert → precondition no longer blocks USB.
    [Fact]
    public async Task Usb_UserNoCccd_OrgCert_Succeeds()
    {
        _userManager.FindByIdAsync(_callerId.ToString())
            .Returns(new ApplicationUser { Id = _callerId, TenantId = _tenantId, SigningNationalId = null });
        var nonce = ValidNonce();
        var orgCert = new CertificateInfo(
            "ORGSER2", "CN=CONG TY XYZ, O=CONG TY XYZ, C=VN", Issuer,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), "THUMBXYZ", [7, 8]);
        StubVerifyValid(true, orgCert);
        _enrollments.AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("USB org cert does not need user CCCD — precondition lifted");
    }

    // ── Idempotency + pending cap ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateActiveSerial_ReturnsExisting_Idempotent()
    {
        var nonce = ValidNonce();
        StubVerifyValid(true, MatchingCert());
        var existing = SignerCertEnrollment.Create(
            _tenantId, _callerId, Serial, Issuer, "T", Subject, _callerId,
            EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
            CertSubjectType.Personal, "Test Signer", Gen);
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.PublicId, "an active row with the same serial+issuer is returned, not duplicated");
        await _enrollments.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingCapReached_Fails()
    {
        var nonce = ValidNonce();
        StubVerifyValid(true, MatchingCert());
        var pending = Enumerable.Range(0, 5)
            .Select(i => SignerCertEnrollment.Create(
                _tenantId, _callerId, $"S{i}", $"I{i}", "T", Subject, _callerId,
                EnrollmentKind.UsbTokenVgca, CertApprovalStatus.Pending,
                CertSubjectType.Personal, "Test Signer", Gen))
            .ToArray();
        _enrollments.GetByUserAsync(_tenantId, _callerId, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await Sut().Handle(UsbCmd(nonce.Id), CancellationToken.None);

        result.Errors.Should().ContainSingle(e => Code(e) == "ENROLLMENT_PENDING_LIMIT_REACHED");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static string? Code(IError e) =>
        e.Metadata.TryGetValue("ErrorCode", out var v) ? v as string : null;

    private static UserManager<ApplicationUser> MakeUserManager()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser>>();
        return Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
    }
}
