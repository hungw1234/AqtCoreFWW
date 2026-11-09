using AqtCoreFW.Identity.Application.Queries.ListDataScopeTypes;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.DataScope;

/// <summary>Handles ListDataScopeTypesQuery — returns all scope type lookup entries.</summary>
public sealed class ListDataScopeTypesQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListDataScopeTypesQuery, Result<IReadOnlyList<DataScopeTypeDto>>>
{
    public async Task<Result<IReadOnlyList<DataScopeTypeDto>>> Handle(
        ListDataScopeTypesQuery request, CancellationToken ct)
    {
        var types = await db.DataScopeTypes
            .OrderBy(t => t.SortOrder)
            .Select(t => new DataScopeTypeDto(t.Id, t.Code, t.Name, t.SortOrder))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<DataScopeTypeDto>>(types);
    }
}
