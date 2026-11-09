using AqtCoreFW.MasterData.Application.Commands.DeleteDistrict;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for DeleteDistrictCommand — soft-deletes district (IsActive = false) and invalidates cache.</summary>
public sealed class DeleteDistrictCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<DeleteDistrictCommand, Result>
{
    public async Task<Result> Handle(DeleteDistrictCommand cmd, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Code == cmd.Code, ct);
        if (district is null)
            return Result.Fail(new NotFoundError($"District '{cmd.Code}' not found"));

        db.Entry(district).CurrentValues["IsActive"] = false;
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:districts");
        return Result.Ok();
    }
}
