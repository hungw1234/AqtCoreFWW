using AqtCoreFW.MasterData.Application.Commands.DeleteProvince;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for DeleteProvinceCommand — soft-deletes province (IsActive = false) and invalidates cache.</summary>
public sealed class DeleteProvinceCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<DeleteProvinceCommand, Result>
{
    public async Task<Result> Handle(DeleteProvinceCommand cmd, CancellationToken ct)
    {
        var province = await db.Provinces.FirstOrDefaultAsync(p => p.Code == cmd.Code, ct);
        if (province is null)
            return Result.Fail(new NotFoundError($"Province '{cmd.Code}' not found"));

        db.Entry(province).CurrentValues["IsActive"] = false;
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:provinces");
        return Result.Ok();
    }
}
