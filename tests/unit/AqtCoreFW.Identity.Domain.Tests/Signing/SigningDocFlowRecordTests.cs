using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="SigningDocFlowRecord"/> state machine.
/// Covers the Supersede guard, IsTerminal invariant, and state transitions.
/// </summary>
public sealed class SigningDocFlowRecordTests
{
    private static readonly Guid Tenant    = Guid.NewGuid();
    private static readonly Guid DocId     = Guid.NewGuid();
    private static readonly Guid UserId    = Guid.NewGuid();
    private static readonly Guid Nonce     = Guid.NewGuid();

    private static SigningDocFlowRecord Initiate(int seq = 1, string? prevKey = null)
        => SigningDocFlowRecord.Initiate(
            tenantId:                Tenant,
            docId:                   DocId,
            signType:                DocFlowSignType.Issued,
            originalStorageKey:      $"{Tenant}/original.pdf",
            jwtNonce:                Guid.NewGuid(), // unique nonce per record
            initiatedBy:             UserId,
            signerSequenceNumber:    seq,
            previousSignedStorageKey: prevKey);

    // ─── IsTerminal ──────────────────────────────────────────────────────────

    [Fact]
    public void IsTerminal_Initiated_ReturnsFalse()
    {
        var record = Initiate();
        record.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void IsTerminal_Downloaded_ReturnsFalse()
    {
        var record = Initiate();
        record.MarkDownloaded();
        record.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void IsTerminal_Completed_ReturnsTrue()
    {
        var record = Initiate();
        record.MarkDownloaded();
        record.Complete($"{Tenant}/{DocId}/signed.pdf", UserId);
        record.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_Failed_ReturnsTrue()
    {
        var record = Initiate();
        record.Fail("upload error", UserId);
        record.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_Expired_ReturnsTrue()
    {
        var record = Initiate();
        record.Expire();
        record.IsTerminal.Should().BeTrue();
    }

    // ─── Supersede ───────────────────────────────────────────────────────────

    [Fact]
    public void Supersede_Initiated_SoftDeletesRecord()
    {
        var record = Initiate();
        record.Supersede(UserId);
        record.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Supersede_Downloaded_SoftDeletesRecord()
    {
        var record = Initiate();
        record.MarkDownloaded();
        record.Supersede(UserId);
        record.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Supersede_Completed_ThrowsInvalidOperation()
    {
        var record = Initiate();
        record.MarkDownloaded();
        record.Complete($"{Tenant}/{DocId}/signed.pdf", UserId);

        var act = () => record.Supersede(UserId);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal*");
    }

    [Fact]
    public void Supersede_Failed_ThrowsInvalidOperation()
    {
        var record = Initiate();
        record.Fail("error", UserId);

        var act = () => record.Supersede(UserId);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal*");
    }

    [Fact]
    public void Supersede_Expired_ThrowsInvalidOperation()
    {
        var record = Initiate();
        record.Expire();

        var act = () => record.Supersede(UserId);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*terminal*");
    }

    // ─── Initiate factory validation ─────────────────────────────────────────

    [Fact]
    public void Initiate_EmptyTenantId_Throws()
    {
        var act = () => SigningDocFlowRecord.Initiate(
            Guid.Empty, DocId, DocFlowSignType.Issued, "tenant/key.pdf", Nonce, UserId);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Initiate_EmptyDocId_Throws()
    {
        var act = () => SigningDocFlowRecord.Initiate(
            Tenant, Guid.Empty, DocFlowSignType.Issued, "tenant/key.pdf", Nonce, UserId);
        act.Should().Throw<ArgumentException>().WithParameterName("docId");
    }

    [Fact]
    public void Initiate_SequenceZero_Throws()
    {
        var act = () => SigningDocFlowRecord.Initiate(
            Tenant, DocId, DocFlowSignType.Issued, "tenant/key.pdf", Nonce, UserId, signerSequenceNumber: 0);
        act.Should().Throw<ArgumentException>().WithParameterName("signerSequenceNumber");
    }
}
