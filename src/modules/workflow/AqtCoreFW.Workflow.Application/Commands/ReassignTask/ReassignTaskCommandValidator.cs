using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.ReassignTask;

public sealed class ReassignTaskCommandValidator : AbstractValidator<ReassignTaskCommand>
{
    public ReassignTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.NewAssigneeId).NotEmpty();
        RuleFor(x => x.ReassignedById).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}
