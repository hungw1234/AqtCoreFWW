using AqtCoreFW.MasterData.Application.Commands.CreateDictionaryItem;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

public sealed class CreateDictionaryItemCommandHandler(MasterDataDbContext db, IGuidGenerator guidGen)
    : IRequestHandler<CreateDictionaryItemCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateDictionaryItemCommand cmd, CancellationToken ct)
    {
        // cmd.DictionaryId is Guid (PublicId); resolve internal long FK
        var dictInternalId = await db.Dictionaries
            .Where(d => d.PublicId == cmd.DictionaryId)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (dictInternalId is null)
            return Result.Fail(new NotFoundError($"Dictionary {cmd.DictionaryId} not found"));

        var codeExists = await db.DictionaryItems.AnyAsync(
            i => i.DictionaryId == dictInternalId.Value && i.Code == cmd.Code, ct);
        if (codeExists)
            return Result.Fail(new ConflictError(
                $"Item with code '{cmd.Code}' already exists in dictionary {cmd.DictionaryId}"));

        long? parentInternalId = null;
        if (cmd.ParentId.HasValue)
        {
            var parent = await db.DictionaryItems
                .Where(i => i.PublicId == cmd.ParentId.Value && i.DictionaryId == dictInternalId.Value)
                .Select(i => (long?)i.Id)
                .FirstOrDefaultAsync(ct);
            if (parent is null)
                return Result.Fail(new ValidationError(
                    $"Parent item {cmd.ParentId} not found in dictionary {cmd.DictionaryId}"));
            parentInternalId = parent;
        }

        var item = DictionaryItem.Create(
            guidGen,
            dictInternalId.Value, cmd.Code, cmd.Name, cmd.NameVi,
            parentInternalId, cmd.SortOrder, cmd.EffectiveFrom,
            cmd.EffectiveTo, cmd.Metadata, cmd.TenantId, cmd.ActorId);

        db.DictionaryItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Result.Ok(item.PublicId);
    }
}
