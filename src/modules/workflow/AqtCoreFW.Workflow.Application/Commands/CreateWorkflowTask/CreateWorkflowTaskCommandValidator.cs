using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;

public sealed class CreateWorkflowTaskCommandValidator : AbstractValidator<CreateWorkflowTaskCommand>
{
    public CreateWorkflowTaskCommandValidator()
    {
        RuleFor(x => x.WorkflowInstancePublicId).NotEmpty(); // Guid.Empty rejected
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
        RuleFor(x => x.DueDate)
            .GreaterThan(DateTime.UtcNow)
            .When(x => x.DueDate.HasValue)
            .WithMessage("DueDate must be in the future.");
    }
}
