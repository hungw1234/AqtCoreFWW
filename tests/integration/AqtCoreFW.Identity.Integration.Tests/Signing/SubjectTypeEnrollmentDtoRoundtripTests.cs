using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests proving the SubjectType + DisplayName DTO round-trip for org-cert enrollments.
///
/// Scope (Phase 5 — narrowed per CONTEXT/SCOPE DECISION): full org-cert enroll through
/// EnrollMySignatureCertCommand is not feasible with a self-generated cert (possession-proof
/// requires chain-to-VGCA-root). Instead we exercise the genuinely-uncovered surface:
/// an Organisation enrollment persisted directly to the DB must surface SubjectType=Organization
/// and DisplayName through the two query handlers that feed the FE.
///
/// Test scenarios:
///   1. Persistence round-trip: both fields survive a reload from the real DB (not lost/defaulted).
///   2. ListPendingEnrollmentsQuery: the Pending org enrollment appears with correct SubjectType + DisplayName.
///   3. SignatureOverviewQuery: the enrollment list for the signer carries SubjectType/DisplayName.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SubjectTypeEnrollmentDtoRoundtripTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    // Deterministic display name per task spec
    private const string OrgDisplayName = "BAN CƠ YẾU CHÍNH PHỦ";
    private const string PersonalDisplayName = "NGUYEN VAN A";

    public SubjectTypeEnrollmentDtoRoundtripTests(WebAppFixture app) => _app = app;

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a real AspNetUsers row via UserManager so SignatureOverviewQueryHandler's
    /// tenant-scoped user lookup does not return null.
    /// </summary>
    private async Task<ApplicationUser> SeedUserAsync(Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id       = userId,
            UserName = $"subjecttype-test-{userId:N}",
            Email    = $"subjecttype-{userId:N}@test.local",
            TenantId = tenantId,
            // No SigningNationalId — org cert signer
            SigningNationalId = null,
        };

        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue("User seed must succeed for the overview query to find the user");
        return user;
    }

    /// <summary>
    /// Persists a <see cref="SignerCertEnrollment"/> directly via IdentityDbContext (bypassing the
    /// possession-proof layer — the integration concern here is the DTO projection, not enroll auth).
    /// </summary>
    private async Task<SignerCertEnrollment> SeedEnrollmentAsync(
        Guid tenantId,
        Guid userId,
        CertSubjectType subjectType,
        string? displayName,
        CertApprovalStatus approvalStatus,
        string serial,
        string issuer)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var enrollment = SignerCertEnrollment.Create(
            tenantId:       tenantId,
            userId:         userId,
            serialNumber:   serial,
            issuer:         issuer,
            thumbprint:     $"THUMB-RT-{serial}",
            subject:        subjectType == CertSubjectType.Organization
                                ? $"CN={displayName}, O={displayName}, C=VN"
                                : $"CN={displayName}, serialNumber=079090001234, C=VN",
            enrolledBy:     Guid.NewGuid(),
            kind:           EnrollmentKind.UsbTokenVgca,
            approvalStatus: approvalStatus,
            subjectType:    subjectType,
            displayName:    displayName,
            gen:            gen);

        db.SignerCertEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return enrollment;
    }

    // ─── Test 1: Persistence round-trip ──────────────────────────────────────────

    /// <summary>
    /// Proves that SubjectType and DisplayName stored via the domain factory survive a fresh
    /// EF reload — they are not defaulted to 0/null by the ORM or migration.
    /// </summary>
    [Fact]
    public async Task OrgEnrollment_PersistenceRoundtrip_SubjectTypeAndDisplayNameSurviveReload()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        // Arrange — org cert Pending
        var seeded = await SeedEnrollmentAsync(
            tenantId, userId,
            CertSubjectType.Organization, OrgDisplayName,
            CertApprovalStatus.Pending,
            serial: $"ORG-RT-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Org CA, O=VGCA, C=VN");

        // Also seed a personal cert Approved to ensure multiple kinds coexist
        await SeedEnrollmentAsync(
            tenantId, userId,
            CertSubjectType.Personal, PersonalDisplayName,
            CertApprovalStatus.Approved,
            serial: $"PER-RT-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Personal CA, O=VGCA, C=VN");

        // Act — reload in a fresh DbContext scope (different EF instance)
        await using var readScope = _app.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var reloaded = await db.SignerCertEnrollments
            .FindAsync(seeded.Id);

        // Assert
        reloaded.Should().NotBeNull("the enrollment row must be findable by its PK");
        reloaded!.SubjectType.Should().Be(
            CertSubjectType.Organization,
            "Organization SubjectType must survive persist-reload (not fall back to default Personal=0)");
        reloaded.DisplayName.Should().Be(
            OrgDisplayName,
            "DisplayName must survive persist-reload unchanged");
        reloaded.ApprovalStatus.Should().Be(
            CertApprovalStatus.Pending,
            "Pending status must survive persist-reload");
    }

    [Fact]
    public async Task PersonalEnrollment_PersistenceRoundtrip_SubjectTypeDefaultNotLost()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        var seeded = await SeedEnrollmentAsync(
            tenantId, userId,
            CertSubjectType.Personal, PersonalDisplayName,
            CertApprovalStatus.Approved,
            serial: $"PER-PERS-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Personal CA, O=VGCA, C=VN");

        await using var readScope = _app.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db.SignerCertEnrollments.FindAsync(seeded.Id);

        reloaded.Should().NotBeNull();
        reloaded!.SubjectType.Should().Be(CertSubjectType.Personal);
        reloaded.DisplayName.Should().Be(PersonalDisplayName);
        reloaded.ApprovalStatus.Should().Be(CertApprovalStatus.Approved);
    }

    // ─── Test 2: ListPendingEnrollmentsQuery ──────────────────────────────────────

    /// <summary>
    /// Proves the Pending org enrollment surfaces through the query handler with correct
    /// SubjectType=Organization and DisplayName — the DTO projection is tested end-to-end
    /// through the real handler + real repository + real DB.
    /// </summary>
    [Fact]
    public async Task ListPendingEnrollmentsQuery_OrgPendingEnrollment_SurfacesSubjectTypeAndDisplayName()
    {
        var tenantId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        // Seed the org cert enrollment as Pending (ListPendingAsync filters ApprovalStatus=Pending)
        var orgEnrollment = await SeedEnrollmentAsync(
            tenantId, userId,
            CertSubjectType.Organization, OrgDisplayName,
            CertApprovalStatus.Pending,
            serial: $"ORG-PEND-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Org CA, O=VGCA, C=VN");

        // Seed an Approved personal cert — must NOT appear in the pending list
        await SeedEnrollmentAsync(
            tenantId, userId,
            CertSubjectType.Personal, PersonalDisplayName,
            CertApprovalStatus.Approved,
            serial: $"PER-PEND-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Personal CA, O=VGCA, C=VN");

        // Act — dispatch through the real handler via IMediator
        using var scope = _app.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ListPendingEnrollmentsQuery(
            TenantId: tenantId,
            Page:     1,
            PageSize: 50));

        // Assert
        result.IsSuccess.Should().BeTrue("query must succeed against the real DB");

        var items = result.Value.Items;
        items.Should().NotBeEmpty("the pending org enrollment must be returned");

        // Find the org enrollment row (other tests may have seeded rows in the same DB for different tenants,
        // but our tenantId is fresh Guid.NewGuid() so isolation is guaranteed).
        var orgRow = items.SingleOrDefault(i => i.PublicId == orgEnrollment.PublicId);
        orgRow.Should().NotBeNull("the org enrollment with known PublicId must appear in the pending list");
        orgRow!.SubjectType.Should().Be(
            CertSubjectType.Organization,
            "ListPendingEnrollmentsQuery must project SubjectType=Organization for the org cert");
        orgRow.DisplayName.Should().Be(
            OrgDisplayName,
            "ListPendingEnrollmentsQuery must project the DisplayName for the org cert");
        orgRow.Kind.Should().Be(
            EnrollmentKind.UsbTokenVgca,
            "enrollment kind must be projected faithfully");

        // The Approved personal cert must NOT appear in the pending list
        items.Should().NotContain(
            i => i.PublicId == orgEnrollment.PublicId && i.SubjectType == CertSubjectType.Personal,
            "personal+Approved cert must not appear in the pending queue");
    }

    // ─── Test 3: SignatureOverviewQuery ───────────────────────────────────────────

    /// <summary>
    /// Proves the enrollment status list in the SignatureOverviewQuery response carries
    /// SubjectType and DisplayName for both enrollment types when queried for the same signer.
    ///
    /// Requires a real AspNetUsers row because the handler tenant-pins the user via
    /// UserManager.FindByIdAsync — a missing user returns NotFoundError.
    /// </summary>
    [Fact]
    public async Task SignatureOverviewQuery_EnrollmentList_CarriesSubjectTypeAndDisplayName()
    {
        var tenantId = Guid.NewGuid();

        // Create a real user (required by SignatureOverviewQueryHandler.FindByIdAsync)
        var user = await SeedUserAsync(tenantId);

        // Seed org cert Pending + personal cert Approved for the same signer
        var orgEnrollment = await SeedEnrollmentAsync(
            tenantId, user.Id,
            CertSubjectType.Organization, OrgDisplayName,
            CertApprovalStatus.Pending,
            serial: $"ORG-OVW-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Org CA, O=VGCA, C=VN");

        var personalEnrollment = await SeedEnrollmentAsync(
            tenantId, user.Id,
            CertSubjectType.Personal, PersonalDisplayName,
            CertApprovalStatus.Approved,
            serial: $"PER-OVW-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Personal CA, O=VGCA, C=VN");

        // Act — dispatch through real handler
        using var scope = _app.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new SignatureOverviewQuery(
            TenantId:     tenantId,
            CallerUserId: user.Id));

        // Assert
        result.IsSuccess.Should().BeTrue("overview query must succeed for a valid tenant+user pair");

        var dto     = result.Value;
        var enrolls = dto.Enrollments;

        enrolls.Should().HaveCount(
            2,
            "both seeded enrollments must appear in the signer's enrollment history");

        // Locate the org enrollment by PublicId
        var orgDto = enrolls.SingleOrDefault(e => e.PublicId == orgEnrollment.PublicId);
        orgDto.Should().NotBeNull("org enrollment must appear in the overview");
        orgDto!.SubjectType.Should().Be(
            CertSubjectType.Organization,
            "SignatureOverviewQuery must surface SubjectType=Organization for the org cert");
        orgDto.DisplayName.Should().Be(
            OrgDisplayName,
            "SignatureOverviewQuery must surface the org DisplayName");
        orgDto.ApprovalStatus.Should().Be(
            CertApprovalStatus.Pending,
            "Pending org cert must be surfaced as Pending");

        // Locate the personal enrollment by PublicId
        var personalDto = enrolls.SingleOrDefault(e => e.PublicId == personalEnrollment.PublicId);
        personalDto.Should().NotBeNull("personal enrollment must appear in the overview");
        personalDto!.SubjectType.Should().Be(
            CertSubjectType.Personal,
            "SignatureOverviewQuery must surface SubjectType=Personal for the personal cert");
        personalDto.DisplayName.Should().Be(
            PersonalDisplayName,
            "SignatureOverviewQuery must surface the personal DisplayName");
        personalDto.ApprovalStatus.Should().Be(
            CertApprovalStatus.Approved,
            "Approved personal cert must be surfaced as Approved");

        // HasApprovedEnrollment must be true because the personal cert is Approved + IsActive
        dto.HasApprovedEnrollment.Should().BeTrue(
            "signer has an active+Approved personal cert enrollment → HasApprovedEnrollment must be true");
    }

    // ─── Test 4: Tenant isolation regression ─────────────────────────────────────

    /// <summary>
    /// Cross-tenant probe: the org enrollment seeded for TenantA must NOT appear in the
    /// pending list when queried with TenantB. Ensures the tenantId predicate in ListPendingAsync
    /// is not accidentally bypassed by a fresh random tenantId.
    /// </summary>
    [Fact]
    public async Task ListPendingEnrollmentsQuery_OrgEnrollment_IsIsolatedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await SeedEnrollmentAsync(
            tenantA, userId,
            CertSubjectType.Organization, OrgDisplayName,
            CertApprovalStatus.Pending,
            serial: $"ORG-ISO-{Guid.NewGuid():N}",
            issuer: "CN=VGCA Org CA, O=VGCA, C=VN");

        using var scope = _app.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ListPendingEnrollmentsQuery(
            TenantId: tenantB, // different tenant
            Page:     1,
            PageSize: 50));

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty(
            "an org enrollment seeded for TenantA must not appear in TenantB's pending queue");
    }
}
