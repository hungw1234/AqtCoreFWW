using AqtCoreFW.Workflow.Application.Validation;
using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;

/// <summary>
/// Validates SaveDefinitionGraphCommand per Phase 02 requirements:
/// - Resolves EntityType from existing WorkflowDefinition row via IWorkflowDefinitionEntityTypeLookup
/// - All state names must match the entity's status enum (case-sensitive)
/// - All transition From/To state names must match the entity's status enum (case-sensitive)
/// Error payloads are structured (D7) with field, value, and allowed list for FE consumption.
/// </summary>
public sealed class SaveDefinitionGraphCommandValidator : AbstractValidator<SaveDefinitionGraphCommand>
{
    private readonly IEntityStatusEnumRegistry _registry;
    private readonly IWorkflowDefinitionEntityTypeLookup _lookup;

    public SaveDefinitionGraphCommandValidator(
        IEntityStatusEnumRegistry registry,
        IWorkflowDefinitionEntityTypeLookup lookup)
    {
        _registry = registry;
        _lookup = lookup;

        RuleFor(x => x.DefinitionId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ModifiedBy).NotEmpty();

        // Basic state field validations
        RuleForEach(x => x.States).ChildRules(state =>
        {
            state.RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
            state.RuleFor(s => s.DisplayNameVi).NotEmpty().MaximumLength(200);
            state.RuleFor(s => s.DisplayNameEn).NotEmpty().MaximumLength(200);
            state.RuleFor(s => s.Color).NotEmpty().MaximumLength(10);
        });

        // Basic transition field validations
        RuleForEach(x => x.Transitions).ChildRules(trans =>
        {
            trans.RuleFor(t => t.FromStateName).NotEmpty().MaximumLength(100);
            trans.RuleFor(t => t.ToStateName).NotEmpty().MaximumLength(100);
            trans.RuleFor(t => t.ActionName).NotEmpty().MaximumLength(100);
            trans.RuleFor(t => t.ActionLabelVi).NotEmpty().MaximumLength(200);
            trans.RuleFor(t => t.ActionLabelEn).NotEmpty().MaximumLength(200);
            trans.RuleFor(t => t.ConditionsJson).MaximumLength(2000);
        });

        // Async rule to look up EntityType and validate state/transition names
        RuleFor(x => x)
            .CustomAsync(ValidateStateAndTransitionNamesAsync);
    }

    private async Task ValidateStateAndTransitionNamesAsync(
        SaveDefinitionGraphCommand cmd,
        ValidationContext<SaveDefinitionGraphCommand> context,
        CancellationToken cancellationToken)
    {
        if (cmd.DefinitionId == Guid.Empty)
            return; // Basic validation will catch this

        // Look up EntityType — tenant-scoped with global template fallback (TenantId=Guid.Empty).
        var entityType = await _lookup.GetEntityTypeAsync(cmd.DefinitionId, cmd.TenantId, cancellationToken);

        if (string.IsNullOrEmpty(entityType))
        {
            context.AddFailure(
                nameof(cmd.DefinitionId),
                $"WorkflowDefinition with ID '{cmd.DefinitionId}' not found.");
            return;
        }

        if (!_registry.IsKnownEntityType(entityType))
        {
            // This shouldn't happen in practice — indicates data corruption or missing contributor
            var knownTypes = string.Join(", ", _registry.GetKnownEntityTypes());
            context.AddFailure(
                nameof(cmd.DefinitionId),
                $"Definition has unknown entity type '{entityType}'. Known types: [{knownTypes}]");
            return;
        }

        var allowedNames = _registry.GetAllowedStateNames(entityType);
        var allowedSet = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        var allowedList = string.Join(", ", allowedNames);

        // Validate state names
        for (int i = 0; i < cmd.States.Count; i++)
        {
            var stateName = cmd.States[i].Name;
            if (!string.IsNullOrEmpty(stateName) && !allowedSet.Contains(stateName))
            {
                context.AddFailure(
                    $"States[{i}].Name",
                    $"Invalid state name '{stateName}' for entity type '{entityType}'. Allowed: [{allowedList}]");
            }
        }

        // Validate transition From/To state names
        for (int i = 0; i < cmd.Transitions.Count; i++)
        {
            var fromState = cmd.Transitions[i].FromStateName;
            var toState = cmd.Transitions[i].ToStateName;

            if (!string.IsNullOrEmpty(fromState) && !allowedSet.Contains(fromState))
            {
                context.AddFailure(
                    $"Transitions[{i}].FromStateName",
                    $"Invalid transition from state '{fromState}' for entity type '{entityType}'. Allowed: [{allowedList}]");
            }

            if (!string.IsNullOrEmpty(toState) && !allowedSet.Contains(toState))
            {
                context.AddFailure(
                    $"Transitions[{i}].ToStateName",
                    $"Invalid transition to state '{toState}' for entity type '{entityType}'. Allowed: [{allowedList}]");
            }
        }
    }
}
