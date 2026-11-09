using FluentValidation;

namespace AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;

public sealed class DeleteAssetManagementCommandValidator
    : AbstractValidator<DeleteAssetManagementCommand>
{
    public DeleteAssetManagementCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0).WithMessage("Mã tài sản không hợp lệ.");
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
    }
}
