using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.SaveQuery;

/// <summary>Saves a pre-approved NLQ query to the QueryCatalog.</summary>
/// <summary>T1 entity — returns long Id (internal PK assigned by EF on SaveChanges).</summary>
public record SaveQueryCommand(
    string Code,
    string Description,
    string DescriptionVi,
    string DapperSql,
    string RequiredParams,
    string OptionalParams,
    string? RequiredRole) : IRequest<Result<long>>;
