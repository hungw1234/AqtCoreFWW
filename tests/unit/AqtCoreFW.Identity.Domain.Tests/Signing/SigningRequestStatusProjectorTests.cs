using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="SigningRequestStatusProjector"/> — verifies the explicit mapping of each underlying
/// state-machine value (and the unlinked/creation-failure cases) onto the unified projection, including the
/// asymmetric cells (DocFlow Downloaded → InProgress, remote Initiated → InProgress).
/// </summary>
public sealed class SigningRequestStatusProjectorTests
{
    [Fact]
    public void ForUnlinked_NoFailure_IsPending()
        => SigningRequestStatusProjector.ForUnlinked(false).Should().Be(SigningRequestStatus.Pending);

    [Fact]
    public void ForUnlinked_WithFailure_IsFailed()
        => SigningRequestStatusProjector.ForUnlinked(true).Should().Be(SigningRequestStatus.Failed);

    [Theory]
    [InlineData(DocFlowStatus.Initiated,  SigningRequestStatus.Pending)]
    [InlineData(DocFlowStatus.Downloaded, SigningRequestStatus.InProgress)]
    [InlineData(DocFlowStatus.Completed,  SigningRequestStatus.Completed)]
    [InlineData(DocFlowStatus.Failed,     SigningRequestStatus.Failed)]
    [InlineData(DocFlowStatus.Expired,    SigningRequestStatus.Expired)]
    public void FromDocFlow_MapsEveryState(DocFlowStatus input, SigningRequestStatus expected)
        => SigningRequestStatusProjector.FromDocFlow(input).Should().Be(expected);

    [Theory]
    [InlineData(PendingTransactionStatus.Initiated, SigningRequestStatus.InProgress)]
    [InlineData(PendingTransactionStatus.Completed, SigningRequestStatus.Completed)]
    [InlineData(PendingTransactionStatus.Failed,    SigningRequestStatus.Failed)]
    [InlineData(PendingTransactionStatus.Expired,   SigningRequestStatus.Expired)]
    public void FromPendingTransaction_MapsEveryState(
        PendingTransactionStatus input, SigningRequestStatus expected)
        => SigningRequestStatusProjector.FromPendingTransaction(input).Should().Be(expected);
}
