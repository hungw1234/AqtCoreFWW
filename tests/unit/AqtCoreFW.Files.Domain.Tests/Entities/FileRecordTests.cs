using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using NSubstitute;

namespace AqtCoreFW.Files.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FileRecord aggregate root.
/// Covers factory method, two-phase status lifecycle, domain events, and soft-delete.
/// Phase-4b B3b: inject IGuidGenerator (NSubstitute); use PublicId (Guid AK) not Id (long PK).
/// Events renamed FileId → FilePublicId per IDomainEvent payload contract change.
/// Phase-02c: MarkAvailable/MarkRejected now require IGuidGenerator param (Rule1 — no Guid.NewGuid() in events).
/// </summary>
public sealed class FileRecordTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UploadedBy = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();

    // Stable PublicId returned by the mock gen so assertions can reference it
    private static readonly Guid StablePublicId = Guid.NewGuid();

    private static IGuidGenerator BuildGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(StablePublicId);
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return gen;
    }

    // --- Factory ---

    private static FileRecord CreateRecord(Guid? caseId = null)
    {
        var gen = BuildGen();
        return FileRecord.Create(
            gen,
            TenantId,
            originalFileName: "document.pdf",
            storageKey: $"{TenantId}/{Guid.NewGuid()}.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            checksumSha256: "abc123",
            uploadedBy: UploadedBy,
            caseId: caseId,
            classification: ClassificationLevel.Internal);
    }

    // --- Create ---

    [Fact]
    public void Create_ValidInput_ReturnsQuarantinedStatus()
    {
        var record = CreateRecord();

        record.Status.Should().Be(FileStatus.Quarantined);
    }

    [Fact]
    public void Create_ValidInput_SetsAllProperties()
    {
        var record = CreateRecord();

        record.TenantId.Should().Be(TenantId);
        record.OriginalFileName.Should().Be("document.pdf");
        record.ContentType.Should().Be("application/pdf");
        record.SizeBytes.Should().Be(1024);
        record.ChecksumSha256.Should().Be("abc123");
        record.UploadedBy.Should().Be(UploadedBy);
        record.ClassificationLevel.Should().Be(ClassificationLevel.Internal);
    }

    [Fact]
    public void Create_ValidInput_SetsPublicId()
    {
        // Phase-4b B3b: T2 entity exposes Guid PublicId (AK set by factory via IGuidGenerator).
        // Id (long) is 0 pre-SaveChanges (DB-assigned IDENTITY).
        var record = CreateRecord();

        record.PublicId.Should().Be(StablePublicId);
    }

    [Fact]
    [Trait("Skip", "Phase-4b B3b: T2 Id is long (bigint IDENTITY, 0 pre-SaveChanges). Use PublicId for uniqueness instead.")]
    public void Create_ValidInput_GeneratesNewFileId()
    {
        // Skipped: long Id = 0 pre-SaveChanges; PublicId is the unique external identifier.
        // See Create_ValidInput_SetsPublicId for the correct assertion.
    }

    [Fact]
    public void Create_EachCall_GeneratesDifferentPublicId()
    {
        // Each call uses a fresh gen returning a distinct Guid
        var gen1 = Substitute.For<IGuidGenerator>();
        var id1 = Guid.NewGuid();
        gen1.NewIdMonotonic().Returns(id1);

        var gen2 = Substitute.For<IGuidGenerator>();
        var id2 = Guid.NewGuid();
        gen2.NewIdMonotonic().Returns(id2);

        var r1 = FileRecord.Create(gen1, TenantId, "a.pdf", "key1", "application/pdf",
            1024, "abc", UploadedBy);
        var r2 = FileRecord.Create(gen2, TenantId, "b.pdf", "key2", "application/pdf",
            1024, "abc", UploadedBy);

        r1.PublicId.Should().NotBe(r2.PublicId);
    }

    [Fact]
    public void Create_WithCaseId_SetsCaseId()
    {
        var record = CreateRecord(caseId: CaseId);

        record.CaseId.Should().Be(CaseId);
    }

    [Fact]
    public void Create_WithoutCaseId_CaseIdIsNull()
    {
        var record = CreateRecord(caseId: null);

        record.CaseId.Should().BeNull();
    }

    [Fact]
    public void Create_SetsCreatedByAuditField()
    {
        var record = CreateRecord();

        record.CreatedBy.Should().Be(UploadedBy);
    }

    [Fact]
    public void Create_NoDomainEventsRaisedOnCreation()
    {
        // FileRecord.Create does not raise any domain event — only MarkAvailable/MarkRejected do
        var record = CreateRecord();

        record.DomainEvents.Should().BeEmpty();
    }

    // --- MarkAvailable ---

    [Fact]
    public void MarkAvailable_FromQuarantined_SetsAvailableStatus()
    {
        var record = CreateRecord();

        record.MarkAvailable(BuildGen());

        record.Status.Should().Be(FileStatus.Available);
    }

    [Fact]
    public void MarkAvailable_RaisesExactlyOneFileUploadedEvent()
    {
        var record = CreateRecord();

        record.MarkAvailable(BuildGen());

        record.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<FileUploadedEvent>();
    }

    [Fact]
    public void MarkAvailable_FileUploadedEvent_HasCorrectPayload()
    {
        var record = CreateRecord();

        record.MarkAvailable(BuildGen());

        // Phase-4b B3b: event field renamed FileId → FilePublicId (IDomainEvent contract).
        var evt = record.DomainEvents.OfType<FileUploadedEvent>().Single();
        evt.FilePublicId.Should().Be(record.PublicId);
        evt.TenantId.Should().Be(TenantId);
        evt.OriginalFileName.Should().Be("document.pdf");
        evt.SizeBytes.Should().Be(1024);
    }

    // --- MarkRejected ---

    [Fact]
    public void MarkRejected_FromQuarantined_SetsRejectedStatus()
    {
        var record = CreateRecord();

        record.MarkRejected(BuildGen());

        record.Status.Should().Be(FileStatus.Rejected);
    }

    [Fact]
    public void MarkRejected_RaisesExactlyOneFileQuarantinedEvent()
    {
        var record = CreateRecord();

        record.MarkRejected(BuildGen());

        record.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<FileQuarantinedEvent>();
    }

    [Fact]
    public void MarkRejected_FileQuarantinedEvent_HasCorrectPayload()
    {
        var record = CreateRecord();

        record.MarkRejected(BuildGen());

        // Phase-4b B3b: event field renamed FileId → FilePublicId.
        var evt = record.DomainEvents.OfType<FileQuarantinedEvent>().Single();
        evt.FilePublicId.Should().Be(record.PublicId);
        evt.TenantId.Should().Be(TenantId);
        evt.OriginalFileName.Should().Be("document.pdf");
    }

    // --- Soft delete ---

    [Fact]
    public void Delete_SoftDeletesRecord()
    {
        var record = CreateRecord();

        record.Delete();

        // ISoftDeletable sets IsDeleted = true via MarkDeleted()
        record.IsDeleted.Should().BeTrue();
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_AfterMarkAvailable_RemovesAllEvents()
    {
        var record = CreateRecord();
        record.MarkAvailable(BuildGen());
        record.DomainEvents.Should().NotBeEmpty();

        record.ClearDomainEvents();

        record.DomainEvents.Should().BeEmpty();
    }
}
