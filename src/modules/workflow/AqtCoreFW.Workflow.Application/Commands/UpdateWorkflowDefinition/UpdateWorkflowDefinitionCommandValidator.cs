using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateWorkflowDefinition;

public sealed class UpdateWorkflowDefinitionCommandValidator : AbstractValidator<UpdateWorkflowDefinitionCommand>
{
    public UpdateWorkflowDefinitionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ModifiedBy).NotEmpty();
    }
}
