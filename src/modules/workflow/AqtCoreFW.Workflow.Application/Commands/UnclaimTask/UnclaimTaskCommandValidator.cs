using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.UnclaimTask;

public sealed class UnclaimTaskCommandValidator : AbstractValidator<UnclaimTaskCommand>
{
    public UnclaimTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).GreaterThan(0);
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
