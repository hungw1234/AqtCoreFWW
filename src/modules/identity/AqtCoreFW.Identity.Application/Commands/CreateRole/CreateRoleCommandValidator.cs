using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.CreateRole;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên vai trò không được để trống")
            .MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().WithMessage("Mã vai trò không được để trống")
            .MaximumLength(100)
            .Matches("^[A-Z0-9_]+$").WithMessage("Mã vai trò phải viết hoa, chỉ gồm A-Z, 0-9, _");
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("TenantId không được để trống");
        RuleFor(x => x.RoleType).IsInEnum().WithMessage("Loại vai trò không hợp lệ");
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
