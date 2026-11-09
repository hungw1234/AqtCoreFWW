using AqtCoreFW.MasterData.Application.Commands.CreateExternalMapping;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

public sealed class CreateExternalMappingCommandHandler(MasterDataDbContext db, IGuidGenerator guidGen)
    : IRequestHandler<CreateExternalMappingCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateExternalMappingCommand cmd, CancellationToken ct)
    {
        long? dictInternalId = null;
        if (cmd.DictionaryId.HasValue)
        {
            // cmd.DictionaryId is Guid (PublicId); look up internal long Id for FK assignment
            var dict = await db.Dictionaries
                .Where(d => d.PublicId == cmd.DictionaryId.Value)
                .Select(d => (long?)d.Id)
                .FirstOrDefaultAsync(ct);
            if (dict is null)
                return Result.Fail(new NotFoundError($"Dictionary {cmd.DictionaryId} not found"));
            dictInternalId = dict;
        }

        var mapping = ExternalMapping.Create(
            guidGen,
            cmd.InternalCode, cmd.ExternalSystem, cmd.ExternalCode,
            cmd.Direction, dictInternalId,
            cmd.ValidFrom, cmd.ValidTo,
            cmd.TenantId, cmd.ActorId);

        db.ExternalMappings.Add(mapping);
        await db.SaveChangesAsync(ct);
        return Result.Ok(mapping.PublicId);
    }
}
