using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for RtbfRequest entity (T3 plain-class: long InternalId PK + Guid MessageId AK).
/// Law 91/2025 Right-to-be-Forgotten.
/// TC-AUD-D004: Create sets Pending status and DueBy 30 days.
/// TC-AUD-D005: StartProcessing transitions to Processing.
/// TC-AUD-D006: Complete sets ProcessedBy, ProcessedAt, Completed.
/// TC-AUD-D007: PartiallyComplete sets failure log.
/// TC-AUD-D008: Reject sets rejection reason.
/// TC-AUD-D009: Create with CitizenNationalId.
/// </summary>
public sealed class RtbfRequestTests
{
    private static readonly Guid TenantId       = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DataSubjectId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProcessorId    = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    private static RtbfRequest Build() => RtbfRequest.Create(NewGen(), TenantId, DataSubjectId);

    // --- TC-AUD-D004: Create sets Pending status and DueBy 30 days ---

    [Fact]
    public void Create_SetsTenantIdAndDataSubjectId()
    {
        var request = Build();

        request.TenantId.Should().Be(TenantId);
        request.DataSubjectId.Should().Be(DataSubjectId);
    }

    [Fact]
    public void Create_StatusIsPending()
    {
        var request = Build();

        request.Status.Should().Be(RtbfStatus.Pending);
    }

    [Fact]
    public void Create_DueBy_IsApproximately30DaysFromNow()
    {
        var before = DateTimeOffset.UtcNow.AddDays(30).AddSeconds(-2);
        var after  = DateTimeOffset.UtcNow.AddDays(30).AddSeconds(2);

        var request = Build();

        request.DueBy.Should().BeAfter(before);
        request.DueBy.Should().BeBefore(after);
    }

    [Fact]
    public void Create_GeneratesNonEmptyMessageId()
    {
        var request = Build();

        // T3: identity key is MessageId (Guid v7), not InternalId (DB-assigned long)
        request.MessageId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ProcessedBy_IsNullInitially()
    {
        var request = Build();

        request.ProcessedBy.Should().BeNull();
    }

    [Fact]
    public void Create_ProcessedAt_IsNullInitially()
    {
        var request = Build();

        request.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void Create_RejectionReason_IsNullInitially()
    {
        var request = Build();

        request.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Create_FailureLog_IsNullInitially()
    {
        var request = Build();

        request.FailureLog.Should().BeNull();
    }

    // --- TC-AUD-D005: StartProcessing transitions to Processing ---

    [Fact]
    public void StartProcessing_TransitionsStatusToProcessing()
    {
        var request = Build();

        request.StartProcessing();

        request.Status.Should().Be(RtbfStatus.Processing);
    }

    [Fact]
    public void StartProcessing_DoesNotSetProcessedBy()
    {
        var request = Build();

        request.StartProcessing();

        request.ProcessedBy.Should().BeNull();
    }

    // --- TC-AUD-D006: Complete sets ProcessedBy, ProcessedAt, Completed ---

    [Fact]
    public void Complete_SetsStatusToCompleted()
    {
        var request = Build();
        request.StartProcessing();

        request.Complete(ProcessorId);

        request.Status.Should().Be(RtbfStatus.Completed);
    }

    [Fact]
    public void Complete_SetsProcessedBy()
    {
        var request = Build();

        request.Complete(ProcessorId);

        request.ProcessedBy.Should().Be(ProcessorId);
    }

    [Fact]
    public void Complete_SetsProcessedAtApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var request = Build();

        request.Complete(ProcessorId);

        request.ProcessedAt.Should().NotBeNull();
        request.ProcessedAt.Should().BeAfter(before);
        request.ProcessedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    // --- TC-AUD-D007: PartiallyComplete sets failure log ---

    [Fact]
    public void PartiallyComplete_SetsStatusToPartiallyCompleted()
    {
        var request = Build();
        request.StartProcessing();

        request.PartiallyComplete(ProcessorId, "Forms module failed: timeout");

        request.Status.Should().Be(RtbfStatus.PartiallyCompleted);
    }

    [Fact]
    public void PartiallyComplete_SetsFailureLog()
    {
        var request = Build();
        const string log = "Cases module failed: connection refused";

        request.PartiallyComplete(ProcessorId, log);

        request.FailureLog.Should().Be(log);
    }

    [Fact]
    public void PartiallyComplete_SetsProcessedBy()
    {
        var request = Build();

        request.PartiallyComplete(ProcessorId, "partial failure");

        request.ProcessedBy.Should().Be(ProcessorId);
    }

    [Fact]
    public void PartiallyComplete_SetsProcessedAtApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var request = Build();

        request.PartiallyComplete(ProcessorId, "some failure");

        request.ProcessedAt.Should().NotBeNull();
        request.ProcessedAt.Should().BeAfter(before);
    }

    // --- TC-AUD-D008: Reject sets rejection reason ---

    [Fact]
    public void Reject_SetsStatusToRejected()
    {
        var request = Build();

        request.Reject(ProcessorId, "Data subject not verified");

        request.Status.Should().Be(RtbfStatus.Rejected);
    }

    [Fact]
    public void Reject_SetsRejectionReason()
    {
        var request = Build();
        const string reason = "Incomplete documentation submitted";

        request.Reject(ProcessorId, reason);

        request.RejectionReason.Should().Be(reason);
    }

    [Fact]
    public void Reject_SetsProcessedBy()
    {
        var request = Build();

        request.Reject(ProcessorId, "reason");

        request.ProcessedBy.Should().Be(ProcessorId);
    }

    [Fact]
    public void Reject_SetsProcessedAtApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var request = Build();

        request.Reject(ProcessorId, "reason");

        request.ProcessedAt.Should().NotBeNull();
        request.ProcessedAt.Should().BeAfter(before);
    }

    // --- TC-AUD-D009: Create with CitizenNationalId ---

    [Fact]
    public void Create_WithCitizenNationalId_StoresCitizenNationalId()
    {
        const string nationalId = "079200012345";

        var request = RtbfRequest.Create(NewGen(), TenantId, DataSubjectId, nationalId);

        request.CitizenNationalId.Should().Be(nationalId);
    }

    [Fact]
    public void Create_WithoutCitizenNationalId_CitizenNationalIdIsNull()
    {
        var request = Build();

        request.CitizenNationalId.Should().BeNull();
    }

    [Fact]
    public void Create_WithCitizenNationalId_StatusStillPending()
    {
        var request = RtbfRequest.Create(NewGen(), TenantId, DataSubjectId, "079200012345");

        request.Status.Should().Be(RtbfStatus.Pending);
    }
}
