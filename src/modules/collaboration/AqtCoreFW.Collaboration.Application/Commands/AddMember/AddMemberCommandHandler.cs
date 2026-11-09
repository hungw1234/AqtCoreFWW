using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.AddMember;

public sealed class AddMemberCommandHandler(IConversationRepository repository)
    : IRequestHandler<AddMemberCommand, Result>
{
    public async Task<Result> Handle(
        AddMemberCommand request,
        CancellationToken cancellationToken)
    {
        // request.ConversationId = PublicId (T2 Guid AK)
        var conversation = await repository.GetByPublicIdAsync(request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Fail(new NotFoundError($"Conversation {request.ConversationId} not found."));

        // Only an Owner may add members — otherwise any member (or, before this gate, any authenticated
        // same-tenant user) could inject arbitrary users, including with Owner role (privilege
        // escalation). Authorize before the duplicate check so a non-owner learns nothing about membership.
        var caller = conversation.Members.FirstOrDefault(m => m.UserId == request.RequestedBy);
        if (caller is null || caller.Role != MemberRole.Owner)
            return Result.Fail(new ForbiddenError(
                $"User {request.RequestedBy} may not add members to conversation {request.ConversationId} (not an owner)."));

        if (conversation.Members.Any(m => m.UserId == request.UserId))
            return Result.Fail(new ConflictError("User is already a member of this conversation."));

        // ConversationMember.Create uses conversation.Id (internal long FK). Previously the created
        // member was discarded (_ =) — it was never attached to the aggregate nor saved, so AddMember
        // silently no-opped. Attach it to the tracked aggregate, then flush so the INSERT is emitted.
        conversation.AddMember(ConversationMember.Create(conversation.Id, request.UserId, request.Role));
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
