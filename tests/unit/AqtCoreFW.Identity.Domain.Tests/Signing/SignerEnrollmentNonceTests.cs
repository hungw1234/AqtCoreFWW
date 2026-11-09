using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

public sealed class SignerEnrollmentNonceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    [Fact]
    public void Issue_SetsBindingAndFutureExpiry()
    {
        var nonce = SignerEnrollmentNonce.Issue(Gen, Tenant, User, new byte[32], ttlSeconds: 300);

        nonce.Id.Should().NotBe(Guid.Empty);
        nonce.TenantId.Should().Be(Tenant);
        nonce.UserId.Should().Be(User);
        nonce.IsConsumed.Should().BeFalse();
        nonce.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(15)]
    public void Issue_RejectsShortChallenge(int length)
    {
        var act = () => SignerEnrollmentNonce.Issue(Gen, Tenant, User, new byte[length]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_RejectsEmptyTenantOrUser()
    {
        ((Action)(() => SignerEnrollmentNonce.Issue(Gen, Guid.Empty, User, new byte[32]))).Should().Throw<ArgumentException>();
        ((Action)(() => SignerEnrollmentNonce.Issue(Gen, Tenant, Guid.Empty, new byte[32]))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsExpired_TrueAtOrPastExpiry()
    {
        var nonce = SignerEnrollmentNonce.Issue(Gen, Tenant, User, new byte[32], ttlSeconds: 300);

        nonce.IsExpired(DateTime.UtcNow).Should().BeFalse();
        nonce.IsExpired(nonce.ExpiresAt).Should().BeTrue("expiry is inclusive (>=)");
        nonce.IsExpired(nonce.ExpiresAt.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var nonce = SignerEnrollmentNonce.Issue(Gen, Tenant, User, new byte[32]);
        var at = DateTime.UtcNow;

        nonce.Consume(at);
        nonce.IsConsumed.Should().BeTrue();
        nonce.ConsumedAt.Should().Be(at);

        var second = () => nonce.Consume(DateTime.UtcNow);
        second.Should().Throw<InvalidOperationException>("a nonce can only be consumed once (single-use)");
    }
}
