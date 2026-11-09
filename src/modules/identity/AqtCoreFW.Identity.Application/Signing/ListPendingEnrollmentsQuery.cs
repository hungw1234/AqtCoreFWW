using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Admin view of the Pending signing-certificate enrollment review queue for the admin's own tenant
/// (<see cref="TenantId"/> from JWT — H4). Paginated with an upper-bound pageSize (M4 — bounds the
/// admin-queue DoS surface). Items expose <see cref="PendingEnrollmentDto.PublicId"/> (M1), never the
/// enumerable long id.
/// </summary>
public sealed record ListPendingEnrollmentsQuery(
    Guid TenantId,
    int Page,
    int PageSize) : IQuery<PagedResult<PendingEnrollmentDto>>;

/// <summary>Read model for one Pending enrollment in the admin review queue.</summary>
public sealed record PendingEnrollmentDto(
    Guid PublicId,
    Guid UserId,
    EnrollmentKind Kind,
    string SerialNumber,
    string Issuer,
    DateTime EnrolledAt,
    CertSubjectType SubjectType,
    string? DisplayName);

public sealed class ListPendingEnrollmentsQueryHandler(ISignerCertEnrollmentRepository enrollments)
    : IRequestHandler<ListPendingEnrollmentsQuery, Result<PagedResult<PendingEnrollmentDto>>>
{
    public async Task<Result<PagedResult<PendingEnrollmentDto>>> Handle(
        ListPendingEnrollmentsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var skip = (page - 1) * pageSize;

        var (items, total) = await enrollments.ListPendingAsync(query.TenantId, skip, pageSize, ct);

        var dtos = items
            .Select(e => new PendingEnrollmentDto(
                e.PublicId, e.UserId, e.Kind, e.SerialNumber, e.Issuer, e.EnrolledAt,
                e.SubjectType, e.DisplayName))
            .ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<PendingEnrollmentDto>(dtos, total, meta));
    }
}
