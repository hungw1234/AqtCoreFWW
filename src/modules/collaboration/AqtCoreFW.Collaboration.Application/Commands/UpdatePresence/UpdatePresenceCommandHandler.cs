using AqtCoreFW.Collaboration.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Commands.UpdatePresence;

public sealed class UpdatePresenceCommandHandler(IPresenceCacheService presenceCache)
    : IRequestHandler<UpdatePresenceCommand, Result>
{
    public async Task<Result> Handle(
        UpdatePresenceCommand request,
        CancellationToken cancellationToken)
    {
        await presenceCache.SetPresenceAsync(request.UserId, request.Status);
        return Result.Ok();
    }
}
