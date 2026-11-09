using AqtCoreFW.MasterData.Application.Commands.PublishDictionary;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>
/// Publishes a Dictionary:
///   1. Validates all active items have no orphan ParentId references
///   2. Transitions status to Published, increments CurrentVersion
/// </summary>
public sealed class PublishDictionaryCommandHandler(MasterDataDbContext db)
    : IRequestHandler<PublishDictionaryCommand, Result<int>>
{
    public async Task<Result<int>> Handle(PublishDictionaryCommand cmd, CancellationToken ct)
    {
        var dict = await db.Dictionaries
            .Include(d => d.Items.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(d => d.PublicId == cmd.Id, ct);

        if (dict is null)
            return Result.Fail<int>(new NotFoundError($"Dictionary {cmd.Id} not found"));

        if (dict.Status == DictionaryStatus.Archived)
            return Result.Fail<int>(new ValidationError("Archived dictionaries cannot be published"));

        // Validate no orphan ParentId references among active items
        var activeItems = dict.Items.Where(i => i.IsActive).ToList();
        var activeItemIds = activeItems.Select(i => i.Id).ToHashSet();
        var orphanItems = activeItems
            .Where(i => i.ParentId.HasValue && !activeItemIds.Contains(i.ParentId.Value))
            .ToList();

        if (orphanItems.Count > 0)
        {
            var orphanCodes = string.Join(", ", orphanItems.Select(i => i.Code));
            return Result.Fail<int>(new ValidationError(
                $"Cannot publish: items have orphan ParentId references: {orphanCodes}"));
        }

        var newVersion = dict.Publish(cmd.ActorId);

        await db.SaveChangesAsync(ct);

        return Result.Ok(newVersion);
    }
}
