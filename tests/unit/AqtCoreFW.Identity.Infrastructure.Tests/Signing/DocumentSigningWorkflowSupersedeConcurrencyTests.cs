using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="DocumentSigningWorkflow"/> supersede-on-reinit path and
/// unique-violation typed conflict surface.
///
/// InMemory database: used for supersede path tests (no SQL Server unique index needed —
/// the test verifies that the active prior record is soft-deleted before the new insert).
/// The unique-violation typed-conflict coverage is validated via the helper method test
/// (SQL error detection logic is pure C# — no DB required).
/// </summary>
public sealed class DocumentSigningWorkflowSupersedeConcurrencyTests
{
    private static readonly Guid Tenant   = Guid.NewGuid();
    private static readonly Guid DocId    = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();
    private const string OriginalKey      = "aa6e2f0a-0000-0000-0000-000000000001/original.pdf";

    private static (DocumentSigningWorkflow sut, IdentityDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db  = new IdentityDbContext(options);
        var jwt = Substitute.For<IDocFlowJwtService>();
        jwt.IssueJwt(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>())
           .Returns("test-jwt");

        var sut = new DocumentSigningWorkflow(db, jwt, NullLogger<DocumentSigningWorkflow>.Instance);
        return (sut, db);
    }

    private static DocFlowMetadata MetaFor(Guid tenantId, Guid? prevKey = null) =>
        new(FileName: null, DocNumber: "VB-001", IssuedDate: new DateTime(2026, 6, 17),
            Extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["originalStorageKey"]       = OriginalKey,
                ["initiatedBy"]              = UserId.ToString(),
                ["tenantId"]                 = tenantId.ToString(),
                ["signerSequenceNumber"]      = "1",
            });

    // ─── Supersede frees the active slot so a re-init succeeds ─────────

    [Fact]
    public async Task InitiateSign_PriorInitiatedRecord_IsSupersededBeforeNewInsert()
    {
        var (sut, db) = Build();

        // First initiation — creates the active record.
        var first = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        first.IsSuccess.Should().BeTrue("first initiation must succeed");

        // Verify the prior record exists and is active (Initiated status).
        var firstId = first.RecordId!.Value;
        var priorRecord = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == firstId);
        priorRecord.Should().NotBeNull();
        priorRecord!.Status.Should().Be(DocFlowStatus.Initiated);
        priorRecord.IsDeleted.Should().BeFalse();

        // Second initiation on same (Tenant, DocId, sequence=1) — should supersede the first.
        var second = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        second.IsSuccess.Should().BeTrue("re-init after abandoned attempt must succeed");
        second.RecordId.Should().NotBe(first.RecordId, "a NEW record is created");

        // Prior record must now be soft-deleted (superseded).
        await db.Entry(priorRecord).ReloadAsync();
        priorRecord.IsDeleted.Should().BeTrue(
            "the prior active record must be soft-deleted by Supersede so the filtered-unique slot is freed");
    }

    [Fact]
    public async Task InitiateSign_PriorDownloadedRecord_IsSupersededBeforeNewInsert()
    {
        var (sut, db) = Build();

        // First initiation + simulate MarkDownloaded (abandoned mid-flow).
        var first = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        first.IsSuccess.Should().BeTrue();

        var firstId2 = first.RecordId!.Value;
        var priorRecord = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .FirstAsync(r => r.Id == firstId2);
        priorRecord.MarkDownloaded();
        await db.SaveChangesAsync();

        // Re-initiation: Downloaded (non-terminal, non-deleted) record must also be superseded.
        var second = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        second.IsSuccess.Should().BeTrue("re-init of a Downloaded (abandoned) record must succeed");

        await db.Entry(priorRecord).ReloadAsync();
        priorRecord.IsDeleted.Should().BeTrue("Downloaded non-terminal record must be superseded");
    }

    [Fact]
    public async Task InitiateSign_PriorCompletedRecord_IsNotSuperseded_AndSucceeds()
    {
        // A Completed terminal record is already outside the filtered-unique set —
        // no supersede needed; a new record should insert cleanly.
        var (sut, db) = Build();

        var first = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        first.IsSuccess.Should().BeTrue();

        var firstId3 = first.RecordId!.Value;
        var completedRecord = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .FirstAsync(r => r.Id == firstId3);
        completedRecord.MarkDownloaded();
        completedRecord.Complete($"{Tenant}/{DocId}/signed.pdf", UserId);
        await db.SaveChangesAsync();

        // Re-initiation with a Completed prior: no supersede guard should fire.
        var second = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        second.IsSuccess.Should().BeTrue("re-init is legal when the prior record is terminal (Completed)");
        second.RecordId.Should().NotBe(first.RecordId);

        // Completed record must NOT be superseded.
        await db.Entry(completedRecord).ReloadAsync();
        completedRecord.IsDeleted.Should().BeFalse(
            "Completed terminal record must NOT be soft-deleted by the supersede path");
    }

    [Fact]
    public async Task InitiateSign_CrossTenantPreviousKey_ReturnsTypedFailure()
    {
        // Defense-in-depth guard: previousSignedStorageKey outside caller tenant prefix must be rejected.
        var otherTenant = Guid.NewGuid();
        var metaWithCrossTenantKey = new DocFlowMetadata(
            FileName: null, DocNumber: "VB-001", IssuedDate: new DateTime(2026, 6, 17),
            Extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["originalStorageKey"]        = OriginalKey,
                ["initiatedBy"]               = UserId.ToString(),
                ["tenantId"]                  = Tenant.ToString(),
                ["signerSequenceNumber"]       = "2",
                ["previousSignedStorageKey"]   = $"{otherTenant}/signed.pdf",
            });

        var (sut, _) = Build();
        var result = await sut.SignIssuedAsync(DocId, metaWithCrossTenantKey);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Cross-tenant");
    }

    // ─── Multi-signer chain: supersede only targets SAME sequence ────────────

    [Fact]
    public async Task InitiateSign_DifferentSequenceNumbers_DoNotSupersedePrior()
    {
        // Sequence 1 record (completed) followed by sequence 2 initiation —
        // sequence 2 must NOT supersede the sequence 1 record.
        var (sut, db) = Build();

        // Build metadata for sequence 2 (multi-signer chain).
        var metaSeq2 = new DocFlowMetadata(
            FileName: null, DocNumber: "VB-001", IssuedDate: new DateTime(2026, 6, 17),
            Extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["originalStorageKey"]       = OriginalKey,
                ["initiatedBy"]              = UserId.ToString(),
                ["tenantId"]                 = Tenant.ToString(),
                ["signerSequenceNumber"]      = "2",
                ["previousSignedStorageKey"]  = $"{Tenant}/{DocId}/signed-by-signer1.pdf",
            });

        // Sequence 1 initiation + complete.
        var seq1Result = await sut.SignIssuedAsync(DocId, MetaFor(Tenant));
        seq1Result.IsSuccess.Should().BeTrue();
        var seq1Id = seq1Result.RecordId!.Value;
        var seq1Record = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .FirstAsync(r => r.Id == seq1Id);
        seq1Record.MarkDownloaded();
        seq1Record.Complete($"{Tenant}/{DocId}/signed-by-signer1.pdf", UserId);
        await db.SaveChangesAsync();

        // Sequence 2 initiation.
        var seq2Result = await sut.SignIssuedAsync(DocId, metaSeq2);
        seq2Result.IsSuccess.Should().BeTrue("sequence 2 initiation must succeed independently of sequence 1");

        // Sequence 1 completed record must NOT be soft-deleted.
        await db.Entry(seq1Record).ReloadAsync();
        seq1Record.IsDeleted.Should().BeFalse(
            "supersede targets only the SAME sequence number — sequence 1 completed record is unaffected");
    }
}

