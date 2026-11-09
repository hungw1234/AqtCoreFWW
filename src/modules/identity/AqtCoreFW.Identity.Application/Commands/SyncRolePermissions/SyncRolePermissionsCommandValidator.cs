using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;

public sealed class SyncRolePermissionsCommandValidator : AbstractValidator<SyncRolePermissionsCommand>
{
    public SyncRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.PermissionIds).NotNull();
        RuleFor(x => x.PermissionIds.Count).LessThanOrEqualTo(500)
            .WithMessage("Không thể gán quá 500 quyền cho một vai trò.");
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Justification).MaximumLength(2000)
            .WithMessage("Lý do xác nhận không được vượt quá 2000 ký tự.");
    }
}
