using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetDashboard;

/// <summary>T1 entity — Id is long internal PK.</summary>
public record GetDashboardQuery(long Id) : IRequest<Result<DashboardLayoutDto>>;
