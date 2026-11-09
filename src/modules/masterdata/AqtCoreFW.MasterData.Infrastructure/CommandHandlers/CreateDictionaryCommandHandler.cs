using AqtCoreFW.MasterData.Application.Commands.CreateDictionary;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

public sealed class CreateDictionaryCommandHandler(MasterDataDbContext db, IGuidGenerator guidGen)
    : IRequestHandler<CreateDictionaryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateDictionaryCommand cmd, CancellationToken ct)
    {
        var exists = await db.Dictionaries
            .AnyAsync(d => d.Code == cmd.Code && d.TenantId == cmd.TenantId, ct);
        if (exists)
            return Result.Fail(new ConflictError(
                $"Dictionary with code '{cmd.Code}' already exists for this tenant"));

        var dict = Dictionary.Create(
            guidGen,
            cmd.Code, cmd.Name, cmd.NameVi,
            cmd.Description, cmd.TenantId,
            cmd.IsSystemDefined, cmd.ActorId);

        db.Dictionaries.Add(dict);
        await db.SaveChangesAsync(ct);
        return Result.Ok(dict.PublicId);
    }
}
