using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;

public sealed class CreateFormTemplateCommandValidator : AbstractValidator<CreateFormTemplateCommand>
{
    public CreateFormTemplateCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên biểu mẫu không được để trống.")
            .MaximumLength(200).WithMessage("Tên biểu mẫu không được vượt quá 200 ký tự.");

        RuleFor(x => x.NameVi)
            .NotEmpty().WithMessage("Tên tiếng Việt không được để trống.")
            .MaximumLength(200).WithMessage("Tên tiếng Việt không được vượt quá 200 ký tự.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã biểu mẫu không được để trống.")
            .MaximumLength(100).WithMessage("Mã biểu mẫu không được vượt quá 100 ký tự.")
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_\-]{0,99}$")
            .WithMessage("Mã biểu mẫu phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_', '-'.");

        RuleFor(x => x.StorageMode)
            .IsInEnum().WithMessage("Chế độ lưu trữ không hợp lệ.");

        RuleFor(x => x.CreatedBy)
            .NotEmpty().WithMessage("CreatedBy không được để trống.");
    }
}
