using AqtCoreFW.MasterData.Application.Commands.CreateDistrict;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for CreateDistrictCommand — creates district and invalidates cache.</summary>
public sealed class CreateDistrictCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<CreateDistrictCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CreateDistrictCommand cmd, CancellationToken ct)
    {
        var exists = await db.Districts.AnyAsync(d => d.Code == cmd.Code, ct);
        if (exists)
            return Result.Fail(new ConflictError($"District '{cmd.Code}' already exists"));

        var district = District.Create(cmd.Code, cmd.ProvinceCode, cmd.NameVi, cmd.NameEn, cmd.SortOrder);
        db.Districts.Add(district);
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:districts");
        return Result.Ok(district.Code);
    }
}
