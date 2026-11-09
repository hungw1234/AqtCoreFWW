using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

// The image bytes/validation teeth (SVG reject, magic-byte mismatch, png/jpeg narrowing) live in the host
// adapter and are proven in UserSignatureImageStoreAdapterTests. These handler tests prove the command
// logic: caller-scoped profile lookup (anti-IDOR), targeted-slot write that preserves the other image,
// store-failure propagation (no persist), and not-owned → NotFound.

public sealed class UploadSignatureImageCommandHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();
    private readonly IUserSignatureImageStore _store = Substitute.For<IUserSignatureImageStore>();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private UploadSignatureImageCommandHandler Sut() => new(_repo, _store);

    [Fact]
    public async Task Handle_Full_StoresAndWritesFullSlot_PreservingInitial_ReturnsPublicId()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(Gen, tenantId, callerId, "Mine", callerId);
        profile.SetImages(null, "existing/initial.png", callerId); // pre-existing initial slot

        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _store.ValidateAndStoreAsync(tenantId, callerId, Arg.Any<byte[]>(), "sig.png", Arg.Any<CancellationToken>())
            .Returns(Result.Ok("tenant/user/new-full.png"));

        var result = await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, profile.PublicId, SignatureVisualType.FullSignature, [1, 2, 3], "sig.png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(profile.PublicId);
        profile.FullSignatureKey.Should().Be("tenant/user/new-full.png");
        profile.InitialSignatureKey.Should().Be("existing/initial.png", "the untouched slot is preserved");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Overwrite_DeletesPriorBlob_AfterSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(Gen, tenantId, callerId, "Mine", callerId);
        profile.SetImages("old/full.png", null, callerId); // an existing full image to be replaced

        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _store.ValidateAndStoreAsync(tenantId, callerId, Arg.Any<byte[]>(), "new.png", Arg.Any<CancellationToken>())
            .Returns(Result.Ok("new/full.png"));

        var result = await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, profile.PublicId, SignatureVisualType.FullSignature, [1, 2, 3], "new.png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.FullSignatureKey.Should().Be("new/full.png");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _store.Received(1).DeleteAsync("old/full.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FirstUploadIntoEmptySlot_DoesNotDelete()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(Gen, tenantId, callerId, "Mine", callerId); // both slots null

        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _store.ValidateAndStoreAsync(tenantId, callerId, Arg.Any<byte[]>(), "first.png", Arg.Any<CancellationToken>())
            .Returns(Result.Ok("first/full.png"));

        await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, profile.PublicId, SignatureVisualType.FullSignature, [1, 2, 3], "first.png"),
            CancellationToken.None);

        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Initial_WritesInitialSlot_PreservingFull()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(Gen, tenantId, callerId, "Mine", callerId);
        profile.SetImages("existing/full.png", null, callerId);

        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _store.ValidateAndStoreAsync(tenantId, callerId, Arg.Any<byte[]>(), "init.jpg", Arg.Any<CancellationToken>())
            .Returns(Result.Ok("tenant/user/new-initial.jpg"));

        var result = await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, profile.PublicId, SignatureVisualType.InitialSignature, [1, 2, 3], "init.jpg"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.InitialSignatureKey.Should().Be("tenant/user/new-initial.jpg");
        profile.FullSignatureKey.Should().Be("existing/full.png", "the untouched slot is preserved");
    }

    [Fact]
    public async Task Handle_NotOwned_ReturnsNotFound_DoesNotStoreOrSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var publicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(tenantId, callerId, publicId, Arg.Any<CancellationToken>())
            .Returns((UserSignatureProfile?)null); // another user's/tenant's profile → scoped null

        var result = await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, publicId, SignatureVisualType.FullSignature, [1, 2, 3], "sig.png"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _store.DidNotReceive().ValidateAndStoreAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StoreRejectsImage_PropagatesError_DoesNotWriteOrSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(Gen, tenantId, callerId, "Mine", callerId);
        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _store.ValidateAndStoreAsync(tenantId, callerId, Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<string>(new ValidationError("Image must be a PNG or JPEG file.", "Content")));

        var result = await Sut().Handle(
            new UploadSignatureImageCommand(
                tenantId, callerId, profile.PublicId, SignatureVisualType.FullSignature, [1, 2, 3], "evil.svg"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        profile.FullSignatureKey.Should().BeNull("a rejected image must never reach the profile");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class UploadSignatureImageCommandValidatorTests
{
    private readonly UploadSignatureImageCommandValidator _validator = new();

    private static UploadSignatureImageCommand Cmd(byte[] content, string fileName = "sig.png",
        SignatureVisualType type = SignatureVisualType.FullSignature) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, content, fileName);

    [Fact]
    public void Valid_PassesValidation() =>
        _validator.Validate(Cmd([1, 2, 3])).IsValid.Should().BeTrue();

    [Fact]
    public void EmptyContent_Fails() =>
        _validator.Validate(Cmd([])).IsValid.Should().BeFalse();

    [Fact]
    public void OversizeContent_Fails() =>
        _validator.Validate(Cmd(new byte[UploadSignatureImageCommandValidator.MaxImageBytes + 1]))
            .IsValid.Should().BeFalse("images over the 500 KB cap are rejected before storage");

    [Fact]
    public void EmptyFileName_Fails() =>
        _validator.Validate(Cmd([1, 2, 3], fileName: "")).IsValid.Should().BeFalse();

    [Fact]
    public void UndefinedVisualType_Fails() =>
        _validator.Validate(Cmd([1, 2, 3], type: (SignatureVisualType)99)).IsValid.Should().BeFalse();
}
