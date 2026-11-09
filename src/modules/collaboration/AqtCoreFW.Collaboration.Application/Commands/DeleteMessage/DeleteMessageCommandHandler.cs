using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.DeleteMessage;

public sealed class DeleteMessageCommandHandler(IMessageRepository repository)
    : IRequestHandler<DeleteMessageCommand, Result>
{
    public async Task<Result> Handle(
        DeleteMessageCommand request,
        CancellationToken cancellationToken)
    {
        // request.MessageId = PublicId (T2 Guid AK)
        var message = await repository.GetByPublicIdAsync(request.MessageId, cancellationToken);

        if (message is null)
            return Result.Fail(new NotFoundError($"Message {request.MessageId} not found."));

        // Ownership gate: only the author may delete their own message. The message is already
        // tenant-scoped by the global query filter on load, so this closes the remaining horizontal
        // (same-tenant) IDOR where any user could delete another user's message by Guid.
        if (message.SenderId != request.UserId)
            return Result.Fail(new ForbiddenError(
                $"User {request.UserId} may not delete message {request.MessageId} (not the author)."));

        message.SoftDelete();
        // The message is tracked (loaded via TrackingQuery) — flush the soft-delete; without this
        // the mutation lives only in memory and is silently dropped when the scope ends.
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
