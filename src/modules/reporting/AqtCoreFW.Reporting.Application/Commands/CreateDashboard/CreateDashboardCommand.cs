using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateDashboard;

/// <summary>T1 entity — returns long Id (internal PK assigned by EF on SaveChanges).</summary>
public record CreateDashboardCommand(
    string Name,
    Guid OwnerId,
    Guid TenantId,
    bool IsDefault = false) : IRequest<Result<long>>;
