using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.DelegateTask;

public sealed class DelegateTaskCommandValidator : AbstractValidator<DelegateTaskCommand>
{
    public DelegateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.DelegateToUserId).NotEmpty();
        RuleFor(x => x.ExpiresAt).GreaterThan(_ => DateTimeOffset.UtcNow)
            .WithMessage("ExpiresAt must be in the future.");
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
