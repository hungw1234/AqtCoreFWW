using AqtCoreFW.Collaboration.Application.Commands.AddMember;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Collaboration.Application.Tests.Commands;

/// <summary>
/// Unit tests for AddMemberCommandHandler.
/// Covers the Owner-only authorization gate, the duplicate-member conflict, not-found, and the
/// happy path (member attached to the aggregate + flushed).
/// </summary>
public sealed class AddMemberCommandHandlerTests
{
    private readonly IConversationRepository _repo;
    private readonly IGuidGenerator _guidGen;
    private readonly AddMemberCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    public AddMemberCommandHandlerTests()
    {
        _repo = Substitute.For<IConversationRepository>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new AddMemberCommandHandler(_repo);
    }

    /// <summary>Conversation whose only member is OwnerId (role Owner), as CreateConversation seeds it.</summary>
    private Conversation MakeConversation()
    {
        var conv = Conversation.Create(_guidGen, ConversationType.Group, "Test", OwnerId, TenantId);
        conv.AddMember(ConversationMember.Create(conv.Id, OwnerId, MemberRole.Owner));
        conv.ClearDomainEvents();
        return conv;
    }

    // --- Happy path: owner adds a new member ---

    [Fact]
    public async Task Handle_OwnerAddsNewMember_PersistsAndSucceeds()
    {
        var conv = MakeConversation();
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);
        var newMember = Guid.NewGuid();

        var cmd = new AddMemberCommand(ConversationId, newMember, MemberRole.Member, OwnerId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        conv.Members.Should().Contain(m => m.UserId == newMember);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Authorization: non-owner member is rejected ---

    [Fact]
    public async Task Handle_CallerIsNonOwnerMember_ReturnsForbidden()
    {
        var conv = MakeConversation();
        var plainMember = Guid.NewGuid();
        conv.AddMember(ConversationMember.Create(conv.Id, plainMember, MemberRole.Member));
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        // plainMember (role Member) tries to add someone — must be rejected
        var cmd = new AddMemberCommand(ConversationId, Guid.NewGuid(), MemberRole.Member, plainMember);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Authorization: non-member caller is rejected ---

    [Fact]
    public async Task Handle_CallerNotAMember_ReturnsForbidden()
    {
        var conv = MakeConversation();
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        var cmd = new AddMemberCommand(ConversationId, Guid.NewGuid(), MemberRole.Member, Guid.NewGuid());
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    // --- Ordering: authz is checked before the duplicate-conflict (non-owner gets 403, not 409) ---

    [Fact]
    public async Task Handle_NonOwnerAddsExistingMember_ReturnsForbiddenNotConflict()
    {
        var conv = MakeConversation();
        var plainMember = Guid.NewGuid();
        var existing = Guid.NewGuid();
        conv.AddMember(ConversationMember.Create(conv.Id, plainMember, MemberRole.Member));
        conv.AddMember(ConversationMember.Create(conv.Id, existing, MemberRole.Member));
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        // A non-owner adds someone who is ALREADY a member: authz must win → Forbidden, never Conflict,
        // so a non-owner cannot probe membership. Pins the gate-before-conflict ordering against reorders.
        var cmd = new AddMemberCommand(ConversationId, existing, MemberRole.Member, plainMember);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
        result.Errors.Should().NotContain(e => e is ConflictError);
    }

    // --- Conflict: owner re-adds an existing member ---

    [Fact]
    public async Task Handle_OwnerAddsExistingMember_ReturnsConflict()
    {
        var conv = MakeConversation();
        var existing = Guid.NewGuid();
        conv.AddMember(ConversationMember.Create(conv.Id, existing, MemberRole.Member));
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>()).Returns(conv);

        var cmd = new AddMemberCommand(ConversationId, existing, MemberRole.Member, OwnerId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError);
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_ConversationNotFound_ReturnsFail()
    {
        _repo.GetByPublicIdAsync(ConversationId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var cmd = new AddMemberCommand(ConversationId, Guid.NewGuid(), MemberRole.Member, OwnerId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }
}
