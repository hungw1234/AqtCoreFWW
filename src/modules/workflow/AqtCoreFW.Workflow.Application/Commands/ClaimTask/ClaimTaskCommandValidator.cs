using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.ClaimTask;

public sealed class ClaimTaskCommandValidator : AbstractValidator<ClaimTaskCommand>
{
    public ClaimTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).GreaterThan(0);
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
