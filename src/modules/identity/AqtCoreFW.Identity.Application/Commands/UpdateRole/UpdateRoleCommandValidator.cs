using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.UpdateRole;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên vai trò không được để trống")
            .MaximumLength(200);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
