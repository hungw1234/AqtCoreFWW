using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Logic teeth for the signature-image read-back query (profile preview path). Proves: owner-scoped lookup
/// (repo receives the caller's tenant/user), 404 when the profile is not the caller's, 404 for an empty slot,
/// and a correct byte round-trip with the content-type re-derived from the stored magic bytes (the store
/// never persists the declared type — S8). Hand-rolled fakes (no DB) mirror the store-adapter test style.
/// </summary>
public sealed class GetMySignatureImageQueryHandlerTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    // This project has no NSubstitute reference and no DI host in this test class — hand-roll a minimal
    // fake (mirrors FakeRepo/FakeStore below). Returns a fixed id; no test asserts on PublicId value here.
    private sealed class FixedGuidGenerator : IGuidGenerator
    {
        private static readonly Guid Fixed = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid NewIdMonotonic() => Fixed;
        public Guid NewIdRandom() => Fixed;
    }

    private static readonly IGuidGenerator Gen = new FixedGuidGenerator();

    private sealed class FakeRepo : IUserSignatureProfileRepository
    {
        public UserSignatureProfile? ToReturn;
        public (Guid TenantId, Guid UserId, Guid PublicId)? LastLookup;

        public Task<UserSignatureProfile?> GetByPublicIdAsync(Guid tenantId, Guid userId, Guid publicId, CancellationToken ct = default)
        {
            LastLookup = (tenantId, userId, publicId);
            return Task.FromResult(ToReturn);
        }

        public Task AddAsync(UserSignatureProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<UserSignatureProfile>> ListByUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserSignatureProfile>>([]);
        public Task<UserSignatureProfile?> GetDefaultAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<UserSignatureProfile?>(null);
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeStore : IUserSignatureImageStore
    {
        public readonly Dictionary<string, byte[]> Objects = new();
        public Task<byte[]> GetAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(Objects[storageKey]);
        public Task<Result<string>> ValidateAndStoreAsync(Guid tenantId, Guid userId, byte[] content, string fileName, CancellationToken ct = default)
            => Task.FromResult(Result.Ok("unused"));
        public Task DeleteAsync(string storageKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static UserSignatureProfile ProfileWithFullImage(Guid tenant, Guid user, string key)
    {
        var p = UserSignatureProfile.Create(Gen, tenant, user, "Work", user);
        p.SetImages(key, null, user);
        return p;
    }

    [Fact]
    public async Task Returns_image_bytes_and_png_content_type_for_a_filled_slot()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var repo = new FakeRepo { ToReturn = ProfileWithFullImage(tenant, user, "k-full") };
        var store = new FakeStore(); store.Objects["k-full"] = PngBytes;
        var handler = new GetMySignatureImageQueryHandler(repo, store);

        var result = await handler.Handle(
            new GetMySignatureImageQuery(tenant, user, Guid.NewGuid(), SignatureVisualType.FullSignature), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Equal(PngBytes);
        result.Value.ContentType.Should().Be("image/png");
        // Owner-scoping: the lookup used the caller's tenant + user (anti-IDOR is enforced in the repo).
        repo.LastLookup!.Value.TenantId.Should().Be(tenant);
        repo.LastLookup!.Value.UserId.Should().Be(user);
    }

    [Fact]
    public async Task Detects_jpeg_content_type_from_magic_bytes()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var repo = new FakeRepo { ToReturn = ProfileWithFullImage(tenant, user, "k-jpg") };
        var store = new FakeStore(); store.Objects["k-jpg"] = JpegBytes;
        var handler = new GetMySignatureImageQueryHandler(repo, store);

        var result = await handler.Handle(
            new GetMySignatureImageQuery(tenant, user, Guid.NewGuid(), SignatureVisualType.FullSignature), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Fails_with_not_found_when_profile_is_not_the_callers()
    {
        var repo = new FakeRepo { ToReturn = null }; // repo scopes to (tenant,user) → null for anyone else's id
        var handler = new GetMySignatureImageQueryHandler(repo, new FakeStore());

        var result = await handler.Handle(
            new GetMySignatureImageQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SignatureVisualType.FullSignature), default);

        result.IsFailed.Should().BeTrue();
        result.HasError<NotFoundError>().Should().BeTrue();
    }

    [Fact]
    public async Task Fails_with_not_found_when_the_requested_slot_is_empty()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        // Profile has a Full image but the Initial slot is empty → requesting Initial is a 404, not a leak.
        var repo = new FakeRepo { ToReturn = ProfileWithFullImage(tenant, user, "k-full") };
        var handler = new GetMySignatureImageQueryHandler(repo, new FakeStore());

        var result = await handler.Handle(
            new GetMySignatureImageQuery(tenant, user, Guid.NewGuid(), SignatureVisualType.InitialSignature), default);

        result.IsFailed.Should().BeTrue();
        result.HasError<NotFoundError>().Should().BeTrue();
    }
}
