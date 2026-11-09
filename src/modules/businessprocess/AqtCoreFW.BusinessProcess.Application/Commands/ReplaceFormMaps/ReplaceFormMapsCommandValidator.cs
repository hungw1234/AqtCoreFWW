using FluentValidation;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ReplaceFormMaps;

/// <summary>
/// FluentValidation for ReplaceFormMapsCommand.
/// Validates individual binding fields and cross-binding uniqueness constraints.
/// Duplicate (WorkflowStateId, FormTemplatePublicId) or (WorkflowTransitionId, FormTemplatePublicId) pairs
/// are rejected with GOV_BP_005.
/// Max 200 total bindings (DOS/size sanity guard).
/// </summary>
public sealed class ReplaceFormMapsCommandValidator : AbstractValidator<ReplaceFormMapsCommand>
{
    public ReplaceFormMapsCommandValidator()
    {
        RuleFor(x => x.BusinessProcessPublicId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ModifiedBy).NotEmpty();

        // Total binding count sanity guard
        RuleFor(x => x)
            .Must(x => x.StateForms.Count + x.TransitionForms.Count <= 200)
            .WithMessage("GOV_BP_005: Total form bindings must not exceed 200.")
            .OverridePropertyName("StateForms");

        // State form binding rules
        RuleForEach(x => x.StateForms).ChildRules(sf =>
        {
            sf.RuleFor(b => b.WorkflowStateId).GreaterThan(0)
                .WithMessage("GOV_BP_005: WorkflowStateId must be a positive integer.");
            sf.RuleFor(b => b.FormTemplatePublicId).NotEmpty()
                .WithMessage("GOV_BP_005: FormTemplatePublicId must not be empty.");
            sf.RuleFor(b => b.Version).GreaterThan(0)
                .WithMessage("GOV_BP_005: Version must be >= 1.");
        });

        // Transition form binding rules
        RuleForEach(x => x.TransitionForms).ChildRules(tf =>
        {
            tf.RuleFor(b => b.WorkflowTransitionId).GreaterThan(0)
                .WithMessage("GOV_BP_005: WorkflowTransitionId must be a positive integer.");
            tf.RuleFor(b => b.FormTemplatePublicId).NotEmpty()
                .WithMessage("GOV_BP_005: FormTemplatePublicId must not be empty.");
            tf.RuleFor(b => b.Version).GreaterThan(0)
                .WithMessage("GOV_BP_005: Version must be >= 1.");
        });

        // Cross-binding uniqueness: no duplicate (WorkflowStateId, FormTemplatePublicId) pairs
        RuleFor(x => x.StateForms)
            .Must(forms =>
            {
                var seen = new HashSet<(long, Guid)>();
                return forms.All(f => seen.Add((f.WorkflowStateId, f.FormTemplatePublicId)));
            })
            .WithMessage("GOV_BP_005: Duplicate (WorkflowStateId, FormTemplatePublicId) pair in StateForms.")
            .When(x => x.StateForms.Count > 0);

        // Cross-binding uniqueness: no duplicate (WorkflowTransitionId, FormTemplatePublicId) pairs
        RuleFor(x => x.TransitionForms)
            .Must(forms =>
            {
                var seen = new HashSet<(long, Guid)>();
                return forms.All(f => seen.Add((f.WorkflowTransitionId, f.FormTemplatePublicId)));
            })
            .WithMessage("GOV_BP_005: Duplicate (WorkflowTransitionId, FormTemplatePublicId) pair in TransitionForms.")
            .When(x => x.TransitionForms.Count > 0);
    }
}
