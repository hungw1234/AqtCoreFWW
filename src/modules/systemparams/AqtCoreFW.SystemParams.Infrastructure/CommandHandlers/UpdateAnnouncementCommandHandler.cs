using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;

namespace AqtCoreFW.SystemParams.Infrastructure.CommandHandlers;

/// <summary>Handler for UpdateAnnouncementCommand — invalidates cache.</summary>
public sealed class UpdateAnnouncementCommandHandler(SystemParamsDbContext db, ICacheService cache)
    : IRequestHandler<UpdateAnnouncementCommand, Result>
{
    private const string ActiveCacheKey = "announcements:active";

    public async Task<Result> Handle(UpdateAnnouncementCommand cmd, CancellationToken ct)
    {
        var announcement = await db.SystemAnnouncements.FindAsync([cmd.Id], ct);
        if (announcement is null)
            return Result.Fail(new NotFoundError($"Announcement {cmd.Id} not found."));

        announcement.Update(cmd.Title, cmd.Content, cmd.Severity, cmd.StartAt, cmd.EndAt, cmd.IsActive);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(ActiveCacheKey, ct);

        return Result.Ok();
    }
}
