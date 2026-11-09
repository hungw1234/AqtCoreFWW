using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Queries.ListPendingAccessReviews;

/// <summary>Returns pending access review records for the QĐ742 periodic review workflow.</summary>
public sealed record ListPendingAccessReviewsQuery(
    Guid? TenantId,
    int Page = 1,
    int PageSize = 20) : IQuery<IReadOnlyList<AccessReviewDto>>;
