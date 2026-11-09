using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateParallelBranch;

[Obsolete("Dormant alongside CreateParallelBranchCommand — superseded by SaveDefinitionGraph branches[].")]
public sealed class CreateParallelBranchCommandValidator : AbstractValidator<CreateParallelBranchCommand>
{
    public CreateParallelBranchCommandValidator()
    {
        RuleFor(x => x.DefinitionId).NotEmpty();
        RuleFor(x => x.BranchingStateId).NotEmpty();
        RuleFor(x => x.ContinuationStateId).NotEmpty();
        RuleFor(x => x.TimeoutMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Children).NotEmpty().WithMessage("At least one branch child is required.");

        RuleForEach(x => x.Children).ChildRules(child =>
        {
            child.RuleFor(c => c.TargetStateId).NotEmpty();
            child.RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
            child.RuleFor(c => c.DisplayNameVi).NotEmpty().MaximumLength(200);
        });
    }
}
