using FluentValidation;

namespace AqtCoreFW.Identity.Application.Queries.SearchUsers;

/// <summary>
/// Validation: Q must be at least 2 chars to prevent expensive prefix-LIKE scans.
/// Failure → ValidationBehavior returns Result.Fail(ValidationError) → ApiControllerBase maps to 422.
/// </summary>
public sealed class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersQueryValidator()
    {
        RuleFor(x => x.Q)
            .NotEmpty().WithMessage("Vui lòng nhập ít nhất 2 ký tự để tìm kiếm.")
            .MinimumLength(2).WithMessage("Vui lòng nhập ít nhất 2 ký tự để tìm kiếm.");
    }
}
