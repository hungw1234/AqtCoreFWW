using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.SyncUserRoles;

public sealed class SyncUserRolesCommandValidator : AbstractValidator<SyncUserRolesCommand>
{
    public SyncUserRolesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Roles).NotNull().NotEmpty().WithMessage("At least one role is required");
        RuleFor(x => x.UpdatedBy).NotEmpty();
    }
}
