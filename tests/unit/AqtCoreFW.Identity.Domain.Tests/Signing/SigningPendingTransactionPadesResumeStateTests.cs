using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Pins the PAdES async-poll resume-state on <see cref="SigningPendingTransaction"/>: the state the completion
/// path needs to splice the CMS + store the signed PDF WITHOUT re-issuing signHash (F1 idempotency). Additive —
/// the DocFlow / async-callback lifecycle (Initiate/Complete/Fail/Expire) is unchanged and these fields stay null.
/// </summary>
public sealed class SigningPendingTransactionPadesResumeStateTests
{
    private static SigningPendingTransaction NewInitiated() => SigningPendingTransaction.Initiate(
        transactionId: "txn-1", tenantId: Guid.NewGuid(), userId: Guid.NewGuid(),
        requestId: "req-1", documentId: "doc-1", ttl: TimeSpan.FromHours(24));

    [Fact]
    public void AttachPadesResumeState_OnInitiated_PersistsAllParts()
    {
        var tx = NewInitiated();
        byte[] attrs = [1, 2, 3];
        byte[] cert = [4, 5, 6];

        tx.AttachPadesResumeState("tenant/doc/abc.placeholder.pdf", 790, 32768, attrs, cert);

        tx.PadesPlaceholderStorageKey.Should().Be("tenant/doc/abc.placeholder.pdf");
        tx.PadesContentsGapStart.Should().Be(790);
        tx.PadesContentsHexLength.Should().Be(32768);
        tx.PadesSignedAttributesDer.Should().Equal(attrs);
        tx.PadesSignerCertDer.Should().Equal(cert);
    }

    [Fact]
    public void AttachPadesResumeState_AfterTerminal_Throws()
    {
        var tx = NewInitiated();
        tx.Complete([9, 9]);

        var act = () => tx.AttachPadesResumeState("k", 1, 1, [1], [1]);
        act.Should().Throw<InvalidOperationException>(because: "resume state may only be set while Initiated");
    }

    [Theory]
    [InlineData("", 1, 1)]      // empty key
    [InlineData("k", 0, 1)]     // non-positive gap
    [InlineData("k", 1, 0)]     // non-positive width
    public void AttachPadesResumeState_InvalidArgs_FailClosed(string key, int gap, int width)
    {
        var tx = NewInitiated();
        var act = () => tx.AttachPadesResumeState(key, gap, width, [1], [1]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AttachPadesResumeState_EmptyDerParts_FailClosed()
    {
        var tx = NewInitiated();
        ((Action)(() => tx.AttachPadesResumeState("k", 1, 1, [], [1]))).Should().Throw<ArgumentException>();
        ((Action)(() => tx.AttachPadesResumeState("k", 1, 1, [1], []))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CompletePades_TransitionsAndRecordsSignedKey()
    {
        var tx = NewInitiated();

        tx.CompletePades([7, 7, 7], "tenant/doc/abc.signed.pdf");

        tx.Status.Should().Be(PendingTransactionStatus.Completed);
        tx.Signature.Should().Equal(7, 7, 7);
        tx.PadesSignedStorageKey.Should().Be("tenant/doc/abc.signed.pdf");
        tx.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void CompletePades_EmptySignedKey_FailClosed_LeavesAggregateUnmutated()
    {
        var tx = NewInitiated();

        var act = () => tx.CompletePades([7], "  ");

        act.Should().Throw<ArgumentException>();
        // Validate-before-mutate: a blank key must NOT flip the aggregate to a keyless Completed state.
        tx.Status.Should().Be(PendingTransactionStatus.Initiated);
        tx.Signature.Should().BeNull();
        tx.PadesSignedStorageKey.Should().BeNull();
        tx.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void AttachPadesResumeState_SecondCall_IsRejected()
    {
        var tx = NewInitiated();
        tx.AttachPadesResumeState("k1", 790, 32768, [1], [2]);

        // A second attach with a different layout would let completion splice the signature into the wrong bytes.
        var act = () => tx.AttachPadesResumeState("k2", 800, 16384, [3], [4]);
        act.Should().Throw<InvalidOperationException>(because: "attach is once-only per transaction");

        tx.PadesPlaceholderStorageKey.Should().Be("k1", because: "the first layout must be preserved");
    }

    [Fact]
    public void Initiate_LeavesPadesResumeStateNull()
    {
        var tx = NewInitiated();

        tx.PadesPlaceholderStorageKey.Should().BeNull();
        tx.PadesSignedAttributesDer.Should().BeNull();
        tx.PadesSignerCertDer.Should().BeNull();
        tx.PadesSignedStorageKey.Should().BeNull();
        tx.PadesContentsGapStart.Should().BeNull();
        tx.PadesContentsHexLength.Should().BeNull();
    }
}
