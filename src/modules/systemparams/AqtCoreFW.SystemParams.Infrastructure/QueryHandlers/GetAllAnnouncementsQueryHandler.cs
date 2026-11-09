using AqtCoreFW.SystemParams.Application.DTOs;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.QueryHandlers;

/// <summary>Handler for GetAllAnnouncementsQuery — admin list.</summary>
public sealed class GetAllAnnouncementsQueryHandler(SystemParamsDbContext db)
    : IRequestHandler<GetAllAnnouncementsQuery, Result<IReadOnlyList<AnnouncementDto>>>
{
    public async Task<Result<IReadOnlyList<AnnouncementDto>>> Handle(
        GetAllAnnouncementsQuery query, CancellationToken ct)
    {
        var result = await db.SystemAnnouncements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnnouncementDto(
                a.Id, a.Title, a.Content,
                a.Severity.ToString(),
                a.StartAt, a.EndAt, a.IsActive, a.TargetRoles))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<AnnouncementDto>>(result);
    }
}
