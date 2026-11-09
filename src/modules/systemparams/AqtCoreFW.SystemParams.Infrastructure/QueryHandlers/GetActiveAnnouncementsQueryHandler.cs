using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.QueryHandlers;

/// <summary>Handler for GetActiveAnnouncementsQuery — cache-first with 1-min TTL.</summary>
public sealed class GetActiveAnnouncementsQueryHandler(SystemParamsDbContext db, ICacheService cache)
    : IRequestHandler<GetActiveAnnouncementsQuery, Result<IReadOnlyList<ActiveAnnouncementDto>>>
{
    private const string ActiveCacheKey = "announcements:active";
    private static readonly TimeSpan ActiveCacheTtl = TimeSpan.FromMinutes(1);

    public async Task<Result<IReadOnlyList<ActiveAnnouncementDto>>> Handle(
        GetActiveAnnouncementsQuery query, CancellationToken ct)
    {
        var cached = await cache.GetAsync<List<ActiveAnnouncementDto>>(ActiveCacheKey, ct);
        if (cached is not null)
            return Result.Ok<IReadOnlyList<ActiveAnnouncementDto>>(cached);

        var now = DateTimeOffset.UtcNow;
        var result = await db.SystemAnnouncements.AsNoTracking()
            .Where(a => a.IsActive
                && (a.StartAt == null || a.StartAt <= now)
                && (a.EndAt == null || a.EndAt >= now))
            .OrderByDescending(a => a.Severity)
            .Select(a => new ActiveAnnouncementDto(
                a.Id, a.Title, a.Content,
                a.Severity.ToString(),
                a.StartAt, a.EndAt, a.TargetRoles))
            .ToListAsync(ct);

        await cache.SetAsync(ActiveCacheKey, result, ActiveCacheTtl, ct);
        return Result.Ok<IReadOnlyList<ActiveAnnouncementDto>>(result);
    }
}
