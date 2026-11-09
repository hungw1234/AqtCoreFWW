using FluentValidation;

namespace AqtCoreFW.ModuleName.Application.Commands.CreateModuleName;

public sealed class CreateModuleNameCommandValidator
    : AbstractValidator<CreateModuleNameCommand>
{
    public CreateModuleNameCommandValidator()
    {
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("Tên không được để trống.")
            .MaximumLength(200).WithMessage("Tên không được vượt quá 200 ký tự.");
        RuleFor(command => command.Description)
            .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        RuleFor(command => command.CreatedBy)
            .NotEmpty().WithMessage("Người tạo không được để trống.");
    }
}
