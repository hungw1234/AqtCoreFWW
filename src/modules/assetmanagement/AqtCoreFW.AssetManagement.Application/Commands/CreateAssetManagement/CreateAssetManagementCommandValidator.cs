using FluentValidation;

namespace AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;

public sealed class CreateAssetManagementCommandValidator
    : AbstractValidator<CreateAssetManagementCommand>
{
    public CreateAssetManagementCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("Tên tài sản không được để trống.")
            .MaximumLength(200).WithMessage("Tên tài sản không được vượt quá 200 ký tự.");
        RuleFor(command => command.Description)
            .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        RuleFor(command => command.CreatedBy)
            .NotEmpty().WithMessage("Người tạo không được để trống.");
    }
}
