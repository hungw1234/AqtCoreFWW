using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

public sealed class IssueEnrollmentNonceCommandHandlerTests
{
    private readonly ISignerEnrollmentNonceRepository _nonces = Substitute.For<ISignerEnrollmentNonceRepository>();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    [Fact]
    public async Task Handle_IssuesUserBoundNonce_ReturnsChallenge()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        SignerEnrollmentNonce? added = null;
        _nonces.AddAsync(Arg.Do<SignerEnrollmentNonce>(n => added = n), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new IssueEnrollmentNonceCommandHandler(_nonces, Gen);
        var result = await sut.Handle(new IssueEnrollmentNonceCommand(tenantId, callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(tenantId);
        added.UserId.Should().Be(callerId, "the nonce is bound to the caller — anti cross-user replay");
        added.Challenge.Should().HaveCountGreaterThanOrEqualTo(16);

        result.Value.NonceId.Should().Be(added.Id);
        Convert.FromBase64String(result.Value.ChallengeBase64).Should().Equal(added.Challenge);
        result.Value.ExpiresAt.Should().Be(added.ExpiresAt);
    }
}
