using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.UpdateViewDefinition;

public sealed class UpdateViewDefinitionCommandValidator
    : AbstractValidator<UpdateViewDefinitionCommand>
{
    public UpdateViewDefinitionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.UpdatedBy)
            .NotEmpty().WithMessage("UpdatedBy không được để trống.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Tên view không được vượt quá 200 ký tự.")
            .When(x => x.Name is not null);

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("PageSize phải lớn hơn 0.")
            .LessThanOrEqualTo(500).WithMessage("PageSize không được vượt quá 500.")
            .When(x => x.PageSize.HasValue);

        RuleFor(x => x.DefaultSortDir)
            .Must(d => d == "asc" || d == "desc")
            .WithMessage("DefaultSortDir phải là 'asc' hoặc 'desc'.")
            .When(x => x.DefaultSortDir is not null);
    }
}
