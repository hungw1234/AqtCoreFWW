using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

public sealed class ListMySignatureProfilesQueryHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    [Fact]
    public async Task Handle_ReturnsCallerProfiles_MappedToDto_ScopedToCaller()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var a = UserSignatureProfile.Create(Gen, tenantId, callerId, "Alpha", callerId);
        var b = UserSignatureProfile.Create(Gen, tenantId, callerId, "Beta", callerId);
        b.MarkDefault(callerId);
        // Alpha carries only a full-signature image; Beta has neither — the DTO must project each slot
        // independently so the tab can badge them separately.
        a.SetImages("tenant/full.png", null, callerId);
        _repo.ListByUserAsync(tenantId, callerId, Arg.Any<CancellationToken>())
            .Returns(new List<UserSignatureProfile> { a, b });

        var sut = new ListMySignatureProfilesQueryHandler(_repo);
        var result = await sut.Handle(new ListMySignatureProfilesQuery(tenantId, callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(d => d.DisplayName == "Alpha" && !d.IsDefault && !d.HasCertLinked
            && d.HasFullImage && !d.HasInitialImage);
        result.Value.Should().Contain(d => d.DisplayName == "Beta" && d.IsDefault
            && !d.HasFullImage && !d.HasInitialImage);
        // Teeth: query is scoped to the caller's (tenant, user) — not any body/route value.
        await _repo.Received(1).ListByUserAsync(tenantId, callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoProfiles_ReturnsEmptyList()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _repo.ListByUserAsync(tenantId, callerId, Arg.Any<CancellationToken>())
            .Returns(new List<UserSignatureProfile>());

        var sut = new ListMySignatureProfilesQueryHandler(_repo);
        var result = await sut.Handle(new ListMySignatureProfilesQuery(tenantId, callerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
