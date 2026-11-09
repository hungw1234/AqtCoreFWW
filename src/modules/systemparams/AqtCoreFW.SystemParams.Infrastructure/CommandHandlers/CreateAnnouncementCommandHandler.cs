using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.SystemParams.Infrastructure.CommandHandlers;

/// <summary>
/// Handler for CreateAnnouncementCommand — validates and invalidates cache.
/// Returns long (T1 bigint PK, DB-assigned post-SaveChanges).
/// </summary>
public sealed class CreateAnnouncementCommandHandler(
    SystemParamsDbContext db,
    ICacheService cache,
    IValidator<CreateAnnouncementCommand> validator)
    : IRequestHandler<CreateAnnouncementCommand, Result<long>>
{
    private const string ActiveCacheKey = "announcements:active";

    public async Task<Result<long>> Handle(CreateAnnouncementCommand cmd, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(cmd, ct);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .Select(e => new ValidationError(e.ErrorMessage, e.PropertyName))
                .Cast<IError>()
                .ToList();
            return Result.Fail(errors);
        }

        var announcement = SystemAnnouncement.Create(
            cmd.Title, cmd.Content, cmd.Severity,
            cmd.StartAt, cmd.EndAt, cmd.TargetRoles);

        db.SystemAnnouncements.Add(announcement);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(ActiveCacheKey, ct);

        return Result.Ok(announcement.Id);
    }
}
