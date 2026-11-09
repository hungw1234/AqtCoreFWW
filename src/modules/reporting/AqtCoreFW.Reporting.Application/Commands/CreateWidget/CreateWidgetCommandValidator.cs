using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.CreateWidget;

public sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DataQuery).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
        RuleFor(x => x.RefreshSeconds).InclusiveBetween(30, 86400);
        RuleFor(x => x.Config).MaximumLength(4000).When(x => x.Config is not null);
    }
}
