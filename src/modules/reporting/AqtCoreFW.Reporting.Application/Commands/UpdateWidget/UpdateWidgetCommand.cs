using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateWidget;

/// <summary>T1 entity — Id is long internal PK (no PublicId).</summary>
public record UpdateWidgetCommand(
    long Id,
    string? Name,
    string? DataQuery,
    string? Config,
    int? RefreshSeconds) : IRequest<Result>;
