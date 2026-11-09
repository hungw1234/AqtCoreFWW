using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.ExecuteSavedQuery;

/// <summary>
/// Executes a pre-approved QueryCatalogEntry by ID.
/// SECURITY: SQL is fetched from DB (admin-approved), never from caller.
/// Parameters are caller-provided but bound via Dapper parameterisation.
/// T1 entity: EntryId is long internal PK (no PublicId on QueryCatalogEntry).
/// </summary>
public record ExecuteSavedQueryQuery(
    long EntryId,
    string TenantId,
    Dictionary<string, object> Parameters) : IRequest<Result<IReadOnlyList<Dictionary<string, object?>>>>;
