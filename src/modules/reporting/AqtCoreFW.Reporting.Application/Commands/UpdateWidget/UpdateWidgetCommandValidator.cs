using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateWidget;

public sealed class UpdateWidgetCommandValidator : AbstractValidator<UpdateWidgetCommand>
{
    public UpdateWidgetCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.DataQuery).MaximumLength(8000).When(x => x.DataQuery is not null);
        RuleFor(x => x.Config).MaximumLength(4000).When(x => x.Config is not null);
        RuleFor(x => x.RefreshSeconds).InclusiveBetween(30, 86400).When(x => x.RefreshSeconds.HasValue);
    }
}
