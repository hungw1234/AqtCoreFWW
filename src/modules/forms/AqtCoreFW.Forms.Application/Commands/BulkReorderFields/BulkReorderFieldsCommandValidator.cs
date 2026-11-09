using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.BulkReorderFields;

public sealed class BulkReorderFieldsCommandValidator : AbstractValidator<BulkReorderFieldsCommand>
{
    public BulkReorderFieldsCommandValidator()
    {
        RuleFor(x => x.TemplatePublicId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one field order item is required");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.FieldId).NotEmpty();
            item.RuleFor(i => i.NewOrder).GreaterThanOrEqualTo(1);
        });
    }
}
