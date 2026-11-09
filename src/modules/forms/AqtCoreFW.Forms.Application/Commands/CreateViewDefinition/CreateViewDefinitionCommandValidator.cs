using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;

public sealed class CreateViewDefinitionCommandValidator
    : AbstractValidator<CreateViewDefinitionCommand>
{
    public CreateViewDefinitionCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.CreatedBy)
            .NotEmpty().WithMessage("CreatedBy không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên view không được để trống.")
            .MaximumLength(200).WithMessage("Tên view không được vượt quá 200 ký tự.");

        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("EntityType không được để trống.")
            .MaximumLength(200).WithMessage("EntityType không được vượt quá 200 ký tự.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Loại view không hợp lệ.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("PageSize phải lớn hơn 0.")
            .LessThanOrEqualTo(500).WithMessage("PageSize không được vượt quá 500.");

        RuleFor(x => x.DefaultSortDir)
            .Must(d => d is null || d == "asc" || d == "desc")
            .WithMessage("DefaultSortDir phải là 'asc' hoặc 'desc'.");

        RuleForEach(x => x.Columns).ChildRules(col =>
        {
            col.RuleFor(c => c.FieldName)
                .NotEmpty().WithMessage("FieldName của column không được để trống.");
            col.RuleFor(c => c.Label)
                .NotEmpty().WithMessage("Label của column không được để trống.");
            col.RuleFor(c => c.Formatter)
                .IsInEnum().WithMessage("Formatter không hợp lệ.");
        });

        RuleForEach(x => x.Filters).ChildRules(f =>
        {
            f.RuleFor(x => x.FieldName)
                .NotEmpty().WithMessage("FieldName của filter không được để trống.");
            f.RuleFor(x => x.Operator)
                .IsInEnum().WithMessage("Operator không hợp lệ.");
        });
    }
}
