using AqtCoreFW.MasterData.Application.Commands.CreateProvince;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for CreateProvinceCommand — creates province and invalidates cache.</summary>
public sealed class CreateProvinceCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<CreateProvinceCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CreateProvinceCommand cmd, CancellationToken ct)
    {
        var exists = await db.Provinces.AnyAsync(p => p.Code == cmd.Code, ct);
        if (exists)
            return Result.Fail(new ConflictError($"Province '{cmd.Code}' already exists"));

        var province = Province.Create(cmd.Code, cmd.NameVi, cmd.NameEn, cmd.SortOrder);
        db.Provinces.Add(province);
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:provinces");
        return Result.Ok(province.Code);
    }
}
