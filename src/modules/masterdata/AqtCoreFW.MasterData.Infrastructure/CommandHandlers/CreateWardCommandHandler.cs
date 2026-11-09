using AqtCoreFW.MasterData.Application.Commands.CreateWard;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for CreateWardCommand — creates ward and invalidates cache.</summary>
public sealed class CreateWardCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<CreateWardCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CreateWardCommand cmd, CancellationToken ct)
    {
        var exists = await db.Wards.AnyAsync(w => w.Code == cmd.Code, ct);
        if (exists)
            return Result.Fail(new ConflictError($"Ward '{cmd.Code}' already exists"));

        var ward = Ward.Create(cmd.Code, cmd.DistrictCode, cmd.NameVi, cmd.NameEn, cmd.SortOrder);
        db.Wards.Add(ward);
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:wards");
        return Result.Ok(ward.Code);
    }
}
