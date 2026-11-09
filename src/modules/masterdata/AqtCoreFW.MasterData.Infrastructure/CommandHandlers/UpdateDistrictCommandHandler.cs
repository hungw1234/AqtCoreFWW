using AqtCoreFW.MasterData.Application.Commands.UpdateDistrict;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for UpdateDistrictCommand — updates district and invalidates cache.</summary>
public sealed class UpdateDistrictCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<UpdateDistrictCommand, Result>
{
    public async Task<Result> Handle(UpdateDistrictCommand cmd, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Code == cmd.Code, ct);
        if (district is null)
            return Result.Fail(new NotFoundError($"District '{cmd.Code}' not found"));

        db.Entry(district).CurrentValues.SetValues(new
        {
            cmd.NameVi,
            cmd.NameEn,
            cmd.SortOrder,
            cmd.ProvinceCode
        });
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:districts");
        return Result.Ok();
    }
}
