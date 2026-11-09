using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, Result<UserDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUserByIdQueryHandler(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<Result<UserDto>> Handle(GetUserByIdQuery query, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(query.UserId.ToString());
        if (user is null)
            return Result.Fail<UserDto>(new NotFoundError($"User {query.UserId} not found"));

        var roles = await _userManager.GetRolesAsync(user);

        return Result.Ok(new UserDto(
            user.Id,
            user.FullName,
            user.Email ?? string.Empty,
            user.DepartmentCode,
            user.ClearanceLevel,
            user.IsActive,
            user.TenantId,
            user.CreatedAtUtc,
            user.LastLoginAt,
            user.PasswordExpiresAt,
            roles.ToList(),
            user.SigningNationalId));
    }
}
