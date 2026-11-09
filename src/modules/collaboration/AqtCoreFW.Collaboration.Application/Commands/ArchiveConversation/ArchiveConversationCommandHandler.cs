using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.ArchiveConversation;

public sealed class ArchiveConversationCommandHandler(IConversationRepository repository)
    : IRequestHandler<ArchiveConversationCommand, Result>
{
    public async Task<Result> Handle(
        ArchiveConversationCommand request,
        CancellationToken cancellationToken)
    {
        // request.ConversationId = PublicId (T2 Guid AK)
        var conversation = await repository.GetByPublicIdAsync(request.ConversationId, cancellationToken);

        if (conversation is null)
            return Result.Fail(new NotFoundError($"Conversation {request.ConversationId} not found."));

        // Only an Owner may archive. IsArchived is a single aggregate-wide flag that hides the
        // conversation for every member — a management action, not a per-user view toggle. Without
        // this gate any authenticated same-tenant user could archive any conversation by Guid.
        var caller = conversation.Members.FirstOrDefault(m => m.UserId == request.RequestedBy);
        if (caller is null || caller.Role != MemberRole.Owner)
            return Result.Fail(new ForbiddenError(
                $"User {request.RequestedBy} may not archive conversation {request.ConversationId} (not an owner)."));

        conversation.Archive();
        // Tracked aggregate (loaded via TrackingQuery) — flush so the archive flag reaches the store.
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
