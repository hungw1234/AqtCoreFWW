using AqtCoreFW.MasterData.Application.Commands.UpdateProvince;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for UpdateProvinceCommand — updates province and invalidates cache.</summary>
public sealed class UpdateProvinceCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<UpdateProvinceCommand, Result>
{
    public async Task<Result> Handle(UpdateProvinceCommand cmd, CancellationToken ct)
    {
        var province = await db.Provinces.FirstOrDefaultAsync(p => p.Code == cmd.Code, ct);
        if (province is null)
            return Result.Fail(new NotFoundError($"Province '{cmd.Code}' not found"));

        db.Entry(province).CurrentValues.SetValues(new
        {
            cmd.NameVi,
            cmd.NameEn,
            cmd.SortOrder
        });
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:provinces");
        return Result.Ok();
    }
}
