using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateDashboard;

public sealed class UpdateDashboardCommandValidator : AbstractValidator<UpdateDashboardCommand>
{
    public UpdateDashboardCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.WidgetPositions).MaximumLength(8000).When(x => x.WidgetPositions is not null);
    }
}
