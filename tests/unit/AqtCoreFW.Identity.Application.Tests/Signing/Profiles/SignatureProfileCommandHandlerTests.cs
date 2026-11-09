using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

// Repo-level IDOR scoping (cross-tenant / cross-user GetByPublicIdAsync → null) is proven with teeth in
// the P1 integration suite (SignatureProfileRepositoryTests). These handler unit tests prove the NEW
// command logic: that each handler forwards the caller's (TenantId, CallerUserId) — injected from the JWT,
// never from the body — into the repo, maps a null lookup to NotFound, and applies the right mutation.

// File-scoped: shared by every test class below that needs a UserSignatureProfile.Create() fixture.
// V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
// other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
file sealed class TestGuidGenerator : IGuidGenerator
{
    public Guid NewIdMonotonic() => Guid.CreateVersion7();
    public Guid NewIdRandom() => Guid.NewGuid();
}

file static class TestGuid
{
    public static readonly IGuidGenerator Gen = new TestGuidGenerator();
}

public sealed class CreateSignatureProfileCommandHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();

    [Fact]
    public async Task Handle_Valid_CreatesProfileForCaller_AndReturnsPublicId()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        UserSignatureProfile? captured = null;
        _repo.AddAsync(Arg.Do<UserSignatureProfile>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new CreateSignatureProfileCommandHandler(_repo, TestGuid.Gen);
        var result = await sut.Handle(
            new CreateSignatureProfileCommand(tenantId, callerId, "My Signature"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tenantId);
        captured.UserId.Should().Be(callerId, "owner is the caller, never a body-supplied id");
        captured.DisplayName.Should().Be("My Signature");
        captured.IsDefault.Should().BeFalse("new profiles are non-default until explicitly promoted");
        result.Value.Should().Be(captured.PublicId);
    }
}

public sealed class UpdateSignatureProfileCommandHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();

    [Fact]
    public async Task Handle_OwnedProfile_Renames_ScopedToCaller()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "Old Name", callerId);
        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);

        var sut = new UpdateSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new UpdateSignatureProfileCommand(tenantId, callerId, profile.PublicId, "New Name"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.DisplayName.Should().Be("New Name");
        // Teeth: the lookup is scoped to the caller's (tenant, user) from the command — not a body value.
        await _repo.Received(1).GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotOwned_ReturnsNotFound_AndDoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var publicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(tenantId, callerId, publicId, Arg.Any<CancellationToken>())
            .Returns((UserSignatureProfile?)null); // repo scoping returns null for another user's/tenant's profile

        var sut = new UpdateSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new UpdateSignatureProfileCommand(tenantId, callerId, publicId, "New Name"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class MarkDefaultSignatureProfileCommandHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();

    [Fact]
    public async Task Handle_NewDefault_ClearsPriorDefaultThenMarks_TwoFlushes()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var target = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "Target", callerId);
        var current = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "Current", callerId);
        current.MarkDefault(callerId);

        _repo.GetByPublicIdAsync(tenantId, callerId, target.PublicId, Arg.Any<CancellationToken>()).Returns(target);
        _repo.GetDefaultAsync(tenantId, callerId, Arg.Any<CancellationToken>()).Returns(current);

        var sut = new MarkDefaultSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new MarkDefaultSignatureProfileCommand(tenantId, callerId, target.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        current.IsDefault.Should().BeFalse("prior default must be cleared");
        target.IsDefault.Should().BeTrue();
        // Clear-then-mark are flushed separately so the filtered unique index never sees two defaults.
        await _repo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoPriorDefault_MarksTarget_SingleFlush()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var target = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "First", callerId);
        _repo.GetByPublicIdAsync(tenantId, callerId, target.PublicId, Arg.Any<CancellationToken>()).Returns(target);
        _repo.GetDefaultAsync(tenantId, callerId, Arg.Any<CancellationToken>())
            .Returns((UserSignatureProfile?)null); // user's first default — nothing to clear

        var sut = new MarkDefaultSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new MarkDefaultSignatureProfileCommand(tenantId, callerId, target.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        target.IsDefault.Should().BeTrue();
        // No prior default → only the mark-new flush, no clear flush.
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyDefault_IsIdempotent_NoSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var target = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "Target", callerId);
        target.MarkDefault(callerId);
        _repo.GetByPublicIdAsync(tenantId, callerId, target.PublicId, Arg.Any<CancellationToken>()).Returns(target);

        var sut = new MarkDefaultSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new MarkDefaultSignatureProfileCommand(tenantId, callerId, target.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetDefaultAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotOwned_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var publicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(tenantId, callerId, publicId, Arg.Any<CancellationToken>())
            .Returns((UserSignatureProfile?)null);

        var sut = new MarkDefaultSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new MarkDefaultSignatureProfileCommand(tenantId, callerId, publicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class SignatureProfileValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_RejectsEmptyOrWhitespaceDisplayName(string displayName)
    {
        var validator = new CreateSignatureProfileCommandValidator();
        var result = validator.Validate(
            new CreateSignatureProfileCommand(Guid.NewGuid(), Guid.NewGuid(), displayName));
        result.IsValid.Should().BeFalse("whitespace-only names must fail validation (→422), not reach the entity factory (→500)");
    }

    [Fact]
    public void Create_AcceptsValidDisplayName()
    {
        var validator = new CreateSignatureProfileCommandValidator();
        var result = validator.Validate(
            new CreateSignatureProfileCommand(Guid.NewGuid(), Guid.NewGuid(), "My Signature"));
        result.IsValid.Should().BeTrue();
    }
}

public sealed class DeleteSignatureProfileCommandHandlerTests
{
    private readonly IUserSignatureProfileRepository _repo = Substitute.For<IUserSignatureProfileRepository>();

    [Fact]
    public async Task Handle_OwnedProfile_SoftDeletes_AndSaves()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var profile = UserSignatureProfile.Create(TestGuid.Gen, tenantId, callerId, "To Delete", callerId);
        _repo.GetByPublicIdAsync(tenantId, callerId, profile.PublicId, Arg.Any<CancellationToken>())
            .Returns(profile);

        var sut = new DeleteSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new DeleteSignatureProfileCommand(tenantId, callerId, profile.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.IsDeleted.Should().BeTrue("soft-delete via ISoftDeletable");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotOwned_ReturnsNotFound_AndDoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var publicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(tenantId, callerId, publicId, Arg.Any<CancellationToken>())
            .Returns((UserSignatureProfile?)null);

        var sut = new DeleteSignatureProfileCommandHandler(_repo);
        var result = await sut.Handle(
            new DeleteSignatureProfileCommand(tenantId, callerId, publicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
