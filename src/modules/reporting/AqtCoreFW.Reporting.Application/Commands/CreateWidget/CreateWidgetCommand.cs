using AqtCoreFW.Reporting.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateWidget;

/// <summary>T1 entity — returns long Id (internal PK assigned by EF on SaveChanges).</summary>
public record CreateWidgetCommand(
    string Name,
    WidgetType Type,
    string DataQuery,
    Guid TenantId,
    Guid CreatedBy,
    string? Config = null,
    int RefreshSeconds = 300) : IRequest<Result<long>>;
