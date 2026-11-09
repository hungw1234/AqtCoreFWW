using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.CreateDashboard;

public sealed class CreateDashboardCommandValidator : AbstractValidator<CreateDashboardCommand>
{
    public CreateDashboardCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
