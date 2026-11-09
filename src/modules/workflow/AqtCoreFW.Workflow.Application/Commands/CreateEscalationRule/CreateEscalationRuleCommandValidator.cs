using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateEscalationRule;

public sealed class CreateEscalationRuleCommandValidator : AbstractValidator<CreateEscalationRuleCommand>
{
    public CreateEscalationRuleCommandValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
        RuleFor(x => x.EscalationAfterMinutes).GreaterThan(0);
        RuleFor(x => x.EscalateToValue).NotEmpty().MaximumLength(500);
        RuleFor(x => x.NotifyVia).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
    }
}
