using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;

/// <summary>Validates batch transition requests: size 1-50, unique instances, required actor.</summary>
public sealed class BatchExecuteTransitionCommandValidator : AbstractValidator<BatchExecuteTransitionCommand>
{
    public const int MaxBatchSize = 50;

    public BatchExecuteTransitionCommandValidator()
    {
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000).When(x => x.Comment is not null);

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one item is required.")
            .Must(items => items.Count <= MaxBatchSize)
            .WithMessage($"Batch size exceeds maximum of {MaxBatchSize}.")
            .Must(items => items.Select(i => i.InstanceId).Distinct().Count() == items.Count)
            .WithMessage("Duplicate InstanceIds are not allowed in a batch.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.InstanceId).NotEmpty();
            item.RuleFor(i => i.TransitionId).NotEmpty();
        });
    }
}
