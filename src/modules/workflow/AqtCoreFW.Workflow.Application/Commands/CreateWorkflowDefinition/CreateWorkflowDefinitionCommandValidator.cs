using AqtCoreFW.Workflow.Application.Validation;
using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;

/// <summary>
/// Validates CreateWorkflowDefinitionCommand per Phase 02 requirements:
/// - EntityType must be known (registered via IEntityStatusEnumContributor)
/// - All state names must match the entity's status enum (case-sensitive)
/// - All transition From/To state names must match the entity's status enum (case-sensitive)
/// Error payloads are structured (D7) with field, value, and allowed list for FE consumption.
/// </summary>
public sealed class CreateWorkflowDefinitionCommandValidator : AbstractValidator<CreateWorkflowDefinitionCommand>
{
    public CreateWorkflowDefinitionCommandValidator(IEntityStatusEnumRegistry registry)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200);

        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();

        // EntityType validation — must be non-empty and known
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("EntityType is required")
            .Must(entityType => registry.IsKnownEntityType(entityType))
            .WithMessage((cmd, entityType) =>
            {
                var knownTypes = string.Join(", ", registry.GetKnownEntityTypes());
                return $"Unknown entity type '{entityType}'. Known types: [{knownTypes}]";
            });

        // State name validation — each state must be in allowed set (case-sensitive)
        RuleForEach(x => x.States)
            .ChildRules(state =>
            {
                state.RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
                state.RuleFor(s => s.DisplayNameVi).NotEmpty().MaximumLength(200);
                state.RuleFor(s => s.DisplayNameEn).NotEmpty().MaximumLength(200);
                state.RuleFor(s => s.Color).NotEmpty().MaximumLength(10);
            });

        // Custom rule for state names against entity's allowed states
        RuleFor(x => x)
            .Custom((cmd, context) =>
            {
                if (!registry.IsKnownEntityType(cmd.EntityType))
                    return; // EntityType error already reported above

                var allowedNames = registry.GetAllowedStateNames(cmd.EntityType);
                var allowedSet = new HashSet<string>(allowedNames, StringComparer.Ordinal);

                for (int i = 0; i < cmd.States.Count; i++)
                {
                    var stateName = cmd.States[i].Name;
                    if (!string.IsNullOrEmpty(stateName) && !allowedSet.Contains(stateName))
                    {
                        var allowedList = string.Join(", ", allowedNames);
                        context.AddFailure(
                            $"States[{i}].Name",
                            $"Invalid state name '{stateName}' for entity type '{cmd.EntityType}'. Allowed: [{allowedList}]");
                    }
                }
            });

        // Transition validation — basic rules
        RuleForEach(x => x.Transitions)
            .ChildRules(trans =>
            {
                trans.RuleFor(t => t.FromStateName).NotEmpty().MaximumLength(100);
                trans.RuleFor(t => t.ToStateName).NotEmpty().MaximumLength(100);
                trans.RuleFor(t => t.ActionName).NotEmpty().MaximumLength(100);
                trans.RuleFor(t => t.ActionLabelVi).NotEmpty().MaximumLength(200);
                trans.RuleFor(t => t.ActionLabelEn).NotEmpty().MaximumLength(200);
                trans.RuleFor(t => t.ConditionsJson).MaximumLength(2000);
            });

        // Custom rule for transition state names against entity's allowed states
        RuleFor(x => x)
            .Custom((cmd, context) =>
            {
                if (!registry.IsKnownEntityType(cmd.EntityType))
                    return; // EntityType error already reported above

                var allowedNames = registry.GetAllowedStateNames(cmd.EntityType);
                var allowedSet = new HashSet<string>(allowedNames, StringComparer.Ordinal);
                var allowedList = string.Join(", ", allowedNames);

                for (int i = 0; i < cmd.Transitions.Count; i++)
                {
                    var fromState = cmd.Transitions[i].FromStateName;
                    var toState = cmd.Transitions[i].ToStateName;

                    if (!string.IsNullOrEmpty(fromState) && !allowedSet.Contains(fromState))
                    {
                        context.AddFailure(
                            $"Transitions[{i}].FromStateName",
                            $"Invalid transition from state '{fromState}' for entity type '{cmd.EntityType}'. Allowed: [{allowedList}]");
                    }

                    if (!string.IsNullOrEmpty(toState) && !allowedSet.Contains(toState))
                    {
                        context.AddFailure(
                            $"Transitions[{i}].ToStateName",
                            $"Invalid transition to state '{toState}' for entity type '{cmd.EntityType}'. Allowed: [{allowedList}]");
                    }
                }
            });
    }
}
