using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="DocFlowJwtService"/> — the HS256 token gate for the VGCA Tier-3
/// doc-flow download/upload handler.
///
/// The decisive coverage here is the issue→validate ROUND-TRIP: it pins that every claim the token
/// is issued with (especially the standard <c>sub</c>) reads back out of <c>TryValidate</c> unchanged.
/// This is the contract the [AllowAnonymous] handler depends on and the only level at which the
/// inbound-claim-mapping pitfall (which rewrites <c>sub</c> on validation) is observable — the HTTP
/// negative tests all assert a 401/404 status that a sub-mapping failure would also produce, so they
/// cannot distinguish a working validator from a broken one.
/// </summary>
public sealed class DocFlowJwtServiceTests
{
    private static readonly Guid UserId   = Guid.Parse("00000000-0000-0000-cafe-000000000001");
    private static readonly Guid DocId    = Guid.Parse("00000000-0000-0000-cafe-000000000002");
    private static readonly Guid Nonce    = Guid.Parse("00000000-0000-0000-cafe-000000000003");
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-cafe-000000000004");

    private static DocFlowJwtService Build() =>
        new(Options.Create(new DocFlowJwtOptions
        {
            SigningKey = "dev-only-docflow-signing-key-至少-16-chars",
            IssuerName = "AqtCoreFW",
            TtlSeconds = 300,
        }));

    [Fact]
    public void IssueThenValidate_RoundTrips_AllClaims()
    {
        var sut = Build();

        var jwt = sut.IssueJwt(UserId, DocId, Nonce, TenantId);

        var ok = sut.TryValidate(jwt, out var userId, out var docId, out var nonce, out var tenantId, out var error);

        ok.Should().BeTrue("a freshly issued token must validate against the same key/issuer");
        error.Should().BeNull();
        // sub is the claim broken by inbound mapping — assert it explicitly survives the round-trip.
        userId.Should().Be(UserId, "the sub claim must read back as the issued userId, not be remapped away");
        docId.Should().Be(DocId);
        nonce.Should().Be(Nonce);
        tenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Validate_WrongKey_Fails()
    {
        var issued = Build().IssueJwt(UserId, DocId, Nonce, TenantId);

        var otherKeyService = new DocFlowJwtService(Options.Create(new DocFlowJwtOptions
        {
            SigningKey = "a-completely-different-signing-key-value",
            IssuerName = "AqtCoreFW",
            TtlSeconds = 300,
        }));

        var ok = otherKeyService.TryValidate(issued, out _, out _, out _, out _, out var error);

        ok.Should().BeFalse("a token signed with a different key must fail HMAC validation");
        error.Should().NotBeNullOrWhiteSpace();
    }
}
