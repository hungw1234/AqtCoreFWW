using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;

namespace AqtCoreFW.SystemParams.Infrastructure.CommandHandlers;

/// <summary>Handler for DeleteAnnouncementCommand — soft-delete via Deactivate().</summary>
public sealed class DeleteAnnouncementCommandHandler(SystemParamsDbContext db, ICacheService cache)
    : IRequestHandler<DeleteAnnouncementCommand, Result>
{
    private const string ActiveCacheKey = "announcements:active";

    public async Task<Result> Handle(DeleteAnnouncementCommand cmd, CancellationToken ct)
    {
        var announcement = await db.SystemAnnouncements.FindAsync([cmd.Id], ct);
        if (announcement is null)
            return Result.Fail(new NotFoundError($"Announcement {cmd.Id} not found."));

        announcement.Deactivate();
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(ActiveCacheKey, ct);

        return Result.Ok();
    }
}
