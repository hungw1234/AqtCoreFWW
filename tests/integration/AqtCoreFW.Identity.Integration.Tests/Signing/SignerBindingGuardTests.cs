using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Integration.Tests.Signing.Fixtures;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests for <see cref="SignerBindingGuard"/> — C1 security slice.
/// Cert-to-signer binding validation for fail-closed anti-impersonation.
///
/// Test scenarios:
///   a) Enrolled cert serial+issuer match → Bound
///   b) Enrolled but different serial → NotBound
///   c) No enrollment + cert subject CCCD == user.SigningNationalId → Bound
///   d) CCCD mismatch (cert CCCD ≠ user.SigningNationalId) → NotBound
///   e) No enrollment + no SigningNationalId → NotBound (fail-closed)
///
/// All tests use real DI + real DB (SQL Server collection).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SignerBindingGuardTests : IClassFixture<WebAppFixture>, IClassFixture<VgcaGoldenSamples>
{
    private readonly WebAppFixture _app;
    private readonly VgcaGoldenSamples _samples;

    public SignerBindingGuardTests(WebAppFixture app, VgcaGoldenSamples samples)
    {
        _app = app;
        _samples = samples;
    }

    private ISignerBindingGuard GetGuard()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISignerBindingGuard>();
    }

    private async Task<ApplicationUser> SeedUserAsync(
        Guid tenantId,
        string? signingNationalId = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"testuser-{userId:N}",
            Email = $"test-{userId:N}@example.com",
            TenantId = tenantId,
            SigningNationalId = signingNationalId,
        };

        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue("User seed should succeed");
        return user;
    }

    private async Task<SignerCertEnrollment> SeedEnrollmentAsync(
        Guid tenantId,
        Guid userId,
        string certSerial,
        string certIssuer,
        CertApprovalStatus approvalStatus = CertApprovalStatus.Approved,
        bool reject = false,
        bool revoke = false,
        CertSubjectType subjectType = CertSubjectType.Personal)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        // Rejected rows must start Pending then transition (the domain forbids creating in a terminal state).
        var createStatus = reject ? CertApprovalStatus.Pending : approvalStatus;
        var enrollment = SignerCertEnrollment.Create(
            tenantId:       tenantId,
            userId:         userId,
            serialNumber:   certSerial,
            issuer:         certIssuer,
            thumbprint:     "TEST-THUMB-" + certSerial,
            subject:        null,
            enrolledBy:     Guid.NewGuid(),
            kind:           EnrollmentKind.RemoteViettelMysign,
            approvalStatus: createStatus,
            subjectType:    subjectType,
            displayName:    null,
            gen:            gen);

        if (reject) enrollment.Reject(Guid.NewGuid(), "rejected for test");

        db.Set<SignerCertEnrollment>().Add(enrollment);
        await db.SaveChangesAsync();

        // Revoke is an UPDATE-time transition: the IsActive column maps with HasSentinel(false) +
        // DEFAULT 1, so a row inserted with IsActive=false is silently coerced back to active by the DB
        // default. Mirror production (revoke happens after the row already exists) — insert active, then
        // revoke + persist the UPDATE so IsActive=false actually lands in the row.
        if (revoke)
        {
            enrollment.Revoke(Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        return enrollment;
    }

    // ── Test a) Enrolled cert serial+issuer match → Bound ────────────────────────

    [Fact]
    public async Task VerifyAsync_EnrolledSerialAndIssuerMatch_ReturnsBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string enrolledSerial = "ABC123DEF456";
        const string enrolledIssuer = "CN=Test CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789");
        await SeedEnrollmentAsync(tenantId, user.Id, enrolledSerial, enrolledIssuer);

        // Cert with matching serial and issuer
        var cert = new CertificateInfo(
            SerialNumber: enrolledSerial,
            Subject: "CN=Nguyen Van A",
            Issuer: enrolledIssuer,
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "ABC123",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeTrue("Matching enrolled cert serial+issuer should be bound");
        verdict.Reason.Should().NotBeNullOrEmpty("Reason should explain the verdict");
    }

    // ── Test b) Enrolled but different serial → NotBound ──────────────────────────

    [Fact]
    public async Task VerifyAsync_EnrolledButDifferentSerial_ReturnsNotBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string enrolledSerial = "ABC123DEF456";
        const string enrolledIssuer = "CN=Test CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789");
        await SeedEnrollmentAsync(tenantId, user.Id, enrolledSerial, enrolledIssuer);

        // Cert with different serial but same issuer
        var cert = new CertificateInfo(
            SerialNumber: "DIFFERENT12345",
            Subject: "CN=Nguyen Van A",
            Issuer: enrolledIssuer,
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "ABC123",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeFalse("Mismatched serial should be not bound");
        verdict.Reason.Should().NotBeNullOrEmpty("Should explain why binding failed");
    }

    // ── Test c) No enrollment + cert subject CCCD == user.SigningNationalId → Bound ─

    [Fact]
    public async Task VerifyAsync_NoEnrollmentButCccdMatches_ReturnsBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string signingNationalId = "123456789";

        var user = await SeedUserAsync(tenantId, signingNationalId: signingNationalId);
        // No enrollment seeded

        // Cert subject contains matching CCCD (per VGCA format)
        var cert = new CertificateInfo(
            SerialNumber: "ANY123",
            Subject: $"CN=Nguyen Van A, serialNumber={signingNationalId}",
            Issuer: "CN=VGCA CA",
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "XYZ789",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeTrue("Cert CCCD matching user.SigningNationalId should be bound");
        verdict.Reason.Should().NotBeNullOrEmpty("Reason should explain the verdict");
    }

    // ── Test d) CCCD mismatch (cert CCCD ≠ user.SigningNationalId) → NotBound ──────

    [Fact]
    public async Task VerifyAsync_CccdMismatch_ReturnsNotBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string userCccd = "111111111";
        const string certCccd = "999999999";  // Different from user

        var user = await SeedUserAsync(tenantId, signingNationalId: userCccd);
        // No enrollment

        // Cert subject contains different CCCD
        var cert = new CertificateInfo(
            SerialNumber: "ANY123",
            Subject: $"CN=Nguyen Van B, serialNumber={certCccd}",
            Issuer: "CN=VGCA CA",
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "XYZ789",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeFalse("Mismatched CCCD should be not bound");
        verdict.Reason.Should().NotBeNullOrEmpty("Should explain why binding failed");
    }

    // ── Test e) No enrollment + no SigningNationalId → NotBound (fail-closed) ──────

    [Fact]
    public async Task VerifyAsync_NoEnrollmentAndNoSigningNationalId_ReturnsNotBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        var user = await SeedUserAsync(tenantId, signingNationalId: null);
        // No enrollment, no SigningNationalId

        // Cert with any subject
        var cert = new CertificateInfo(
            SerialNumber: "ANY123",
            Subject: "CN=Nguyen Van A",
            Issuer: "CN=VGCA CA",
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "XYZ789",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeFalse(
            "Signer with no enrollment and no SigningNationalId should fail-closed (NotBound)");
        verdict.Reason.Should().NotBeNullOrEmpty("Should explain why binding failed");
    }

    // ── Test: Multiple enrollments, correct one matches ──────────────────────────

    [Fact]
    public async Task VerifyAsync_MultipleEnrollmentsCorrectOneMatches_ReturnsBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string correctSerial = "CORRECT123";
        const string correctIssuer = "CN=Correct CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789");

        // Seed multiple enrollments (stale ones)
        await SeedEnrollmentAsync(tenantId, user.Id, "OLD001", "CN=Old CA");
        await SeedEnrollmentAsync(tenantId, user.Id, "OLD002", "CN=Old CA 2");
        // Seed the current one
        await SeedEnrollmentAsync(tenantId, user.Id, correctSerial, correctIssuer);

        var cert = new CertificateInfo(
            SerialNumber: correctSerial,
            Subject: "CN=Nguyen Van A",
            Issuer: correctIssuer,
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "CURR",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert
        verdict.IsBound.Should().BeTrue("Correct enrollment among many should be found");
    }

    // ── Test: Pending enrollment (not yet admin-approved) → NotBound ─────────────

    [Fact]
    public async Task VerifyAsync_PendingEnrollmentMatchingCert_ReturnsNotBound()
    {
        // A self-service enrollment is Pending until an admin approves it. Even though the cert serial+issuer
        // match the enrolled row, a Pending enrollment must NOT bind — only Approved does (the core P3 tighten).
        // Teeth: the pre-tightening guard (IsActive-only) bound this; now it must be NotBound.
        var tenantId = Guid.NewGuid();
        const string serial = "PENDING123";
        const string issuer = "CN=Test CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789");
        await SeedEnrollmentAsync(tenantId, user.Id, serial, issuer, approvalStatus: CertApprovalStatus.Pending);

        var cert = new CertificateInfo(
            SerialNumber: serial, Subject: "CN=Nguyen Van A", Issuer: issuer,
            NotBefore: DateTime.UtcNow.AddDays(-1), NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "PEND", RawData: [0x30, 0x82]);

        var verdict = await GetGuard().VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        verdict.IsBound.Should().BeFalse("a Pending enrollment must not bind until an admin approves it");
    }

    // ── Test: Rejected enrollment → NotBound (no bootstrap rescue) ────────────────

    [Fact]
    public async Task VerifyAsync_RejectedEnrollmentMatchingCert_ReturnsNotBound()
    {
        var tenantId = Guid.NewGuid();
        const string serial = "REJECTED123";
        const string issuer = "CN=Test CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789");
        await SeedEnrollmentAsync(tenantId, user.Id, serial, issuer, reject: true);

        var cert = new CertificateInfo(
            SerialNumber: serial, Subject: "CN=Nguyen Van A", Issuer: issuer,
            NotBefore: DateTime.UtcNow.AddDays(-1), NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "REJ", RawData: [0x30, 0x82]);

        var verdict = await GetGuard().VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        verdict.IsBound.Should().BeFalse("a Rejected enrollment must not bind");
    }

    // ── Test f) H1: revoked-only row must NOT fall back to bootstrap → NotBound ────

    [Fact]
    public async Task VerifyAsync_RevokedOnlyRow_DoesNotFallBackToBootstrap_ReturnsNotBound()
    {
        // H1: the signer once enrolled (now revoked → IsActive=false) AND has a matching admin-set CCCD.
        // The pre-tightening guard used GetActiveByUserAsync (0 active rows) → silently fell back to the
        // CCCD bootstrap → Bound (wrong). The tightened guard decides the tier via HasAnyEnrollmentAsync
        // (the revoked row still counts) → enrolled-tier governs → no approved match → NotBound.
        // Teeth: this is RED on the old GetActiveByUserAsync-based tier decision.
        var tenantId = Guid.NewGuid();
        const string nationalId = "123456789";
        const string serial = "REVOKED123";
        const string issuer = "CN=Test CA";

        var user = await SeedUserAsync(tenantId, signingNationalId: nationalId);
        await SeedEnrollmentAsync(tenantId, user.Id, serial, issuer, revoke: true);

        // A cert whose subject CCCD matches the user — would bind via bootstrap if the guard fell back.
        var cert = new CertificateInfo(
            SerialNumber: "SOMETHING-ELSE", Subject: $"CN=Nguyen Van A, serialNumber={nationalId}",
            Issuer: "CN=VGCA CA", NotBefore: DateTime.UtcNow.AddDays(-1), NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "REV", RawData: [0x30, 0x82]);

        var verdict = await GetGuard().VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        verdict.IsBound.Should().BeFalse(
            "a revoked-only enrollment keeps the signer in the enrolled tier — no silent bootstrap fallback (H1)");
    }

    // ── Test: Enrolled + Approved Organisation cert (no CCCD in subject) → Bound ──
    // Proves the enrolled tier is cert-type-agnostic: binding key is serial/issuer, not the subject DN.
    // An org cert enrolled + admin-approved binds without any CCCD in the cert subject or the user.

    [Fact]
    public async Task VerifyAsync_EnrolledApprovedOrgCert_NoCccdInSubject_ReturnsBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string orgSerial = "ORG-SERIAL-001";
        const string orgIssuer = "CN=VGCA Org CA, O=VGCA, C=VN";

        // User has no CCCD stored — purely org signer
        var user = await SeedUserAsync(tenantId, signingNationalId: null);
        // Enrollment with Organization subject type, Approved status
        await SeedEnrollmentAsync(tenantId, user.Id, orgSerial, orgIssuer,
            subjectType: CertSubjectType.Organization);

        // Org cert: subject contains O= and CN= but NO serialNumber RDN (no CCCD/national-id)
        var cert = new CertificateInfo(
            SerialNumber: orgSerial,
            Subject:      "CN=VN Company JSC, O=VN Company JSC, C=VN",
            Issuer:       orgIssuer,
            NotBefore:    DateTime.UtcNow.AddDays(-1),
            NotAfter:     DateTime.UtcNow.AddYears(1),
            Thumbprint:   "ORG-THUMB-001",
            RawData:      [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert — enrolled tier matches on serial+issuer; no CCCD required
        verdict.IsBound.Should().BeTrue(
            "an enrolled+Approved org cert with matching serial/issuer must bind via enrolled tier " +
            "even though the subject DN contains no CCCD national-id RDN");
        verdict.Reason.Should().NotBeNullOrEmpty();
    }

    // ── Test: Zero-enrollment signer + org cert (no CCCD) → NotBound (fail-closed) ─
    // Proves org certs MUST enroll: the bootstrap tier cannot bind an org cert because org subjects
    // carry no national-id RDN — TryExtractNationalId returns false → fail-closed.

    [Fact]
    public async Task VerifyAsync_ZeroEnrollmentOrgCert_NoCccdInSubject_ReturnsNotBound()
    {
        // Arrange — no enrollment seeded for this user
        var tenantId = Guid.NewGuid();

        // Even if the user had a SigningNationalId set, the org cert subject has no CCCD RDN,
        // so TryExtractNationalId will fail and the bootstrap cannot produce a match.
        var user = await SeedUserAsync(tenantId, signingNationalId: "123456789012");

        // Org cert: subject has O= / CN= but no serialNumber RDN
        var cert = new CertificateInfo(
            SerialNumber: "ORG-NO-ENROLL-001",
            Subject:      "CN=Another Corp, O=Another Corp, C=VN",
            Issuer:       "CN=VGCA Org CA, O=VGCA, C=VN",
            NotBefore:    DateTime.UtcNow.AddDays(-1),
            NotAfter:     DateTime.UtcNow.AddYears(1),
            Thumbprint:   "ORG-NO-ENROLL-THUMB",
            RawData:      [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert — bootstrap cannot extract a national-id from the org cert subject → NotBound
        verdict.IsBound.Should().BeFalse(
            "an org cert with no CCCD in subject and no enrollment must be fail-closed: " +
            "the bootstrap tier requires a national-id RDN which org certs do not carry");
        verdict.Reason.Should().NotBeNullOrEmpty();
    }

    // ── Test: Personal enrolled cert (regression) → still Bound ─────────────────────
    // Regression guard: adding org-cert logic must not break personal cert enrolled binding.

    [Fact]
    public async Task VerifyAsync_PersonalEnrolledCert_WithCccdInSubject_ReturnsBound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string personalSerial = "PERSONAL-SERIAL-001";
        const string personalIssuer = "CN=VGCA Personal CA, O=VGCA, C=VN";

        var user = await SeedUserAsync(tenantId, signingNationalId: "079090001234");
        await SeedEnrollmentAsync(tenantId, user.Id, personalSerial, personalIssuer,
            subjectType: CertSubjectType.Personal);

        // Personal cert: subject contains serialNumber RDN with CCCD — but binding is via serial/issuer
        var cert = new CertificateInfo(
            SerialNumber: personalSerial,
            Subject:      "CN=Nguyen Van C, serialNumber=079090001234, C=VN",
            Issuer:       personalIssuer,
            NotBefore:    DateTime.UtcNow.AddDays(-1),
            NotAfter:     DateTime.UtcNow.AddYears(1),
            Thumbprint:   "PERSONAL-THUMB-001",
            RawData:      [0x30, 0x82]);

        var guard = GetGuard();

        // Act
        var verdict = await guard.VerifyAsync(cert, user.Id, tenantId, CancellationToken.None);

        // Assert — enrolled tier matches serial+issuer; no regression from org-cert changes
        verdict.IsBound.Should().BeTrue(
            "enrolled personal cert should still bind via serial/issuer match (regression)");
        verdict.Reason.Should().NotBeNullOrEmpty();
    }

    // ── Test: Tenant isolation (cert from different tenant) ──────────────────────

    [Fact]
    public async Task VerifyAsync_UserFromDifferentTenant_ReturnsNotBound()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string signingNationalId = "123456789";

        var userA = await SeedUserAsync(tenantA, signingNationalId: signingNationalId);

        // Cert subject contains the matching CCCD (would bind via bootstrap if tenant matched)
        var cert = new CertificateInfo(
            SerialNumber: "ABC123",
            Subject: $"CN=User A, serialNumber={signingNationalId}",
            Issuer: "CN=VGCA CA",
            NotBefore: DateTime.UtcNow.AddDays(-1),
            NotAfter: DateTime.UtcNow.AddYears(1),
            Thumbprint: "ABC",
            RawData: [0x30, 0x82]);

        var guard = GetGuard();

        // Act — verify with tenantB (different from user's tenant)
        var verdict = await guard.VerifyAsync(cert, userA.Id, tenantB, CancellationToken.None);

        // Assert — must reject because user belongs to tenantA, verified against tenantB
        verdict.IsBound.Should().BeFalse(
            "User from different tenant should not bind even with matching CCCD");
        verdict.Reason.Should().NotBeNullOrEmpty("Should explain why binding failed due to tenant mismatch");
    }
}
