using FluentValidation;

namespace AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;

public sealed class UpdateAssetManagementCommandValidator
    : AbstractValidator<UpdateAssetManagementCommand>
{
    public UpdateAssetManagementCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0).WithMessage("Mã tài sản không hợp lệ.");
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("Tên tài sản không được để trống.")
            .MaximumLength(200).WithMessage("Tên tài sản không được vượt quá 200 ký tự.");
        RuleFor(command => command.Description)
            .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        RuleFor(command => command.UpdatedBy)
            .NotEmpty().WithMessage("Người cập nhật không được để trống.");
    }
}
