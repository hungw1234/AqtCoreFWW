using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetTenantById;

/// <summary>Query to get a single tenant by Id.</summary>
public sealed record GetTenantByIdQuery(Guid TenantId) : IRequest<Result<TenantDetailDto>>;
