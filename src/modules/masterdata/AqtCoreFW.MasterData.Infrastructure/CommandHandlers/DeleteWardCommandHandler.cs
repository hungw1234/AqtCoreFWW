using AqtCoreFW.MasterData.Application.Commands.DeleteWard;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Infrastructure.CommandHandlers;

/// <summary>Handler for DeleteWardCommand — soft-deletes ward (IsActive = false) and invalidates cache.</summary>
public sealed class DeleteWardCommandHandler(MasterDataDbContext db, IMemoryCache memoryCache)
    : IRequestHandler<DeleteWardCommand, Result>
{
    public async Task<Result> Handle(DeleteWardCommand cmd, CancellationToken ct)
    {
        var ward = await db.Wards.FirstOrDefaultAsync(w => w.Code == cmd.Code, ct);
        if (ward is null)
            return Result.Fail(new NotFoundError($"Ward '{cmd.Code}' not found"));

        db.Entry(ward).CurrentValues["IsActive"] = false;
        await db.SaveChangesAsync(ct);

        memoryCache.Remove("masterdata:wards");
        return Result.Ok();
    }
}
