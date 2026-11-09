using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateDashboard;

/// <summary>T1 entity — Id is long internal PK (no PublicId).</summary>
public record UpdateDashboardCommand(
    long Id,
    string? Name,
    string? WidgetPositions,
    bool? IsDefault) : IRequest<Result>;
