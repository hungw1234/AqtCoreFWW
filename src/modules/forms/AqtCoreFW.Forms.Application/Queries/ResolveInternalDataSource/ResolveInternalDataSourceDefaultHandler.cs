using AqtCoreFW.Forms.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.ResolveInternalDataSource;

/// <summary>
/// Fallback handler — returns empty list when no module-specific handler is registered.
/// Individual modules override this by registering their own IRequestHandler for
/// ResolveInternalDataSourceQuery filtered on Module/Entity.
/// </summary>
public sealed class ResolveInternalDataSourceDefaultHandler
    : IRequestHandler<ResolveInternalDataSourceQuery, Result<IReadOnlyList<DataSourceOptionDto>>>
{
    public Task<Result<IReadOnlyList<DataSourceOptionDto>>> Handle(
        ResolveInternalDataSourceQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Ok<IReadOnlyList<DataSourceOptionDto>>([]));
}
