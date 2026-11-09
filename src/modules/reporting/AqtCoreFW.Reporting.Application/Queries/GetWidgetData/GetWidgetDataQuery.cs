using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetWidgetData;

/// <summary>T1 entity: WidgetId is long internal PK. TenantId is Guid FK exception.</summary>
public record GetWidgetDataQuery(long WidgetId, Guid TenantId) : IRequest<Result<WidgetDataDto>>;
