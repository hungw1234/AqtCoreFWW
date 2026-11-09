using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for AuditLogEntry entity (T3 plain-class: long InternalId PK + Guid MessageId AK).
/// TC-AUD-D001: Create sets all required fields.
/// TC-AUD-D002: SetHmacSignature updates signature.
/// TC-AUD-D003: SetSequenceId sets monotonic sequence.
/// </summary>
public sealed class AuditLogEntryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Returns a stub IGuidGenerator that returns a fresh Guid on each NewIdMonotonic() call.</summary>
    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    private static AuditLogEntry BuildEntry(
        Guid? tenantId = null,
        Guid? userId = null,
        string userName = "test.user",
        string action = "CREATE",
        string moduleName = "Cases",
        string resourceType = "Case",
        string? resourceId = "case-001",
        string? dataSnapshot = null,
        string? ipAddress = "10.0.0.1",
        string? correlationId = "corr-xyz") =>
        AuditLogEntry.Create(
            NewGen(),
            tenantId ?? TenantId,
            userId ?? UserId,
            userName,
            action,
            moduleName,
            resourceType,
            resourceId,
            dataSnapshot,
            ipAddress,
            correlationId);

    // --- TC-AUD-D001: Create sets all required fields ---

    [Fact]
    public void Create_SetsAllRequiredFields()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var entry = BuildEntry();

        entry.TenantId.Should().Be(TenantId);
        entry.UserId.Should().Be(UserId);
        entry.UserName.Should().Be("test.user");
        entry.Action.Should().Be("CREATE");
        entry.ModuleName.Should().Be("Cases");
        entry.ResourceType.Should().Be("Case");
        entry.ResourceId.Should().Be("case-001");
        entry.IpAddress.Should().Be("10.0.0.1");
        entry.CorrelationId.Should().Be("corr-xyz");
        entry.OccurredAt.Should().BeAfter(before);
    }

    [Fact]
    public void Create_GeneratesNonEmptyMessageId()
    {
        var entry = BuildEntry();

        // T3: identity key is MessageId (Guid v7), not InternalId (DB-assigned long)
        entry.MessageId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_TwoEntries_HaveDifferentMessageIds()
    {
        var a = BuildEntry();
        var b = BuildEntry();

        a.MessageId.Should().NotBe(b.MessageId);
    }

    [Fact]
    public void Create_HmacSignature_IsEmptyInitially()
    {
        var entry = BuildEntry();

        // Signature is set later by HmacChainService after chain computation
        entry.HmacSignature.Should().BeEmpty();
    }

    [Fact]
    public void Create_SequenceId_IsZeroInitially()
    {
        var entry = BuildEntry();

        entry.SequenceId.Should().Be(0);
    }

    [Fact]
    public void Create_WithNullTenantId_TenantIdIsNull()
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            null, UserId, "user", "LOGIN", "Identity", "Session",
            null, null, null, null);

        entry.TenantId.Should().BeNull();
    }

    [Fact]
    public void Create_WithNullUserId_UserIdIsNull()
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            TenantId, null, "anonymous", "READ", "Forms", "Form",
            null, null, null, null);

        entry.UserId.Should().BeNull();
    }

    [Fact]
    public void Create_OccurredAt_IsApproximatelyUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var entry = BuildEntry();

        entry.OccurredAt.Should().BeAfter(before);
        entry.OccurredAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    // --- TC-AUD-D002: SetHmacSignature updates signature ---

    [Fact]
    public void SetHmacSignature_UpdatesSignatureProperty()
    {
        var entry = BuildEntry();
        const string signature = "sha256-abc123def456";

        entry.SetHmacSignature(signature);

        entry.HmacSignature.Should().Be(signature);
    }

    [Fact]
    public void SetHmacSignature_CanBeCalledMultipleTimes_LastValueWins()
    {
        var entry = BuildEntry();

        entry.SetHmacSignature("first-sig");
        entry.SetHmacSignature("second-sig");

        entry.HmacSignature.Should().Be("second-sig");
    }

    [Fact]
    public void SetHmacSignature_EmptyString_SetsEmptySignature()
    {
        var entry = BuildEntry();
        entry.SetHmacSignature("initial-sig");

        entry.SetHmacSignature(string.Empty);

        entry.HmacSignature.Should().BeEmpty();
    }

    // --- TC-AUD-D003: SetSequenceId sets monotonic sequence ---

    [Fact]
    public void SetSequenceId_SetsSequenceIdProperty()
    {
        var entry = BuildEntry();

        entry.SetSequenceId(42L);

        entry.SequenceId.Should().Be(42L);
    }

    [Fact]
    public void SetSequenceId_LargeValue_Accepted()
    {
        var entry = BuildEntry();
        const long largeSeq = 9_999_999_999L;

        entry.SetSequenceId(largeSeq);

        entry.SequenceId.Should().Be(largeSeq);
    }

    [Fact]
    public void SetSequenceId_MonotonicAssignment_LastValueWins()
    {
        var entry = BuildEntry();

        entry.SetSequenceId(100L);
        entry.SetSequenceId(101L);

        entry.SequenceId.Should().Be(101L);
    }
}
