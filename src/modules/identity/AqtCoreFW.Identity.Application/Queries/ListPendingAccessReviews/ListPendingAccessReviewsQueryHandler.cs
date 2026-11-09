using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Queries.ListPendingAccessReviews;

public sealed class ListPendingAccessReviewsQueryHandler
    : IRequestHandler<ListPendingAccessReviewsQuery, Result<IReadOnlyList<AccessReviewDto>>>
{
    private readonly IAccessReviewRepository _repo;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListPendingAccessReviewsQueryHandler(
        IAccessReviewRepository repo,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        _repo = repo;
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task<Result<IReadOnlyList<AccessReviewDto>>> Handle(
        ListPendingAccessReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var records = await _repo.ListPendingAsync(
            request.TenantId, page, pageSize, cancellationToken);

        var dtos = new List<AccessReviewDto>(records.Count);
        foreach (var r in records)
        {
            var user = await _userManager.FindByIdAsync(r.UserId.ToString());
            var role = await _roleManager.FindByIdAsync(r.RoleId.ToString());
            dtos.Add(new AccessReviewDto(
                r.PublicId,     // T2: DTO.Id mapped FROM PublicId (Guid AK)
                r.UserId,
                user?.Email,
                r.RoleId,
                role?.Name,
                r.NextReviewDue,
                r.CreatedAt));  // Entity base uses CreatedAt (not CreatedAtUtc)
        }

        return Result.Ok<IReadOnlyList<AccessReviewDto>>(dtos);
    }
}
