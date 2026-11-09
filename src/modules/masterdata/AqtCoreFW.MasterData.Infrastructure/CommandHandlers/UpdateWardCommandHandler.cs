using AqtCoreFW.MasterData.Application.Commands.UpdateWard;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for UpdateWardCommand — updates ward and invalidates cache.</summary>
public sealed class UpdateWardCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<UpdateWardCommand, Result>
{
    public async Task<Result> Handle(UpdateWardCommand cmd, CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Code == cmd.Code, ct);
        if (ward is null)
            return Result.Fail(new NotFoundError($"Ward '{cmd.Code}' not found"));

        db.Entry(ward).CurrentValues.SetValues(new
        {
            cmd.NameVi,
            cmd.NameEn,
            cmd.SortOrder,
            cmd.DistrictCode
        });
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:wards");
        return Result.Ok();
    }
}
