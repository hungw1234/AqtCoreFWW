using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;

public sealed class CreateVersionMigrationCommandValidator
    : AbstractValidator<CreateVersionMigrationCommand>
{
    public CreateVersionMigrationCommandValidator()
    {
        RuleFor(x => x.SourceDefinitionId).NotEmpty();
        RuleFor(x => x.TargetDefinitionId).NotEmpty();
        RuleFor(x => x.TargetDefinitionId)
            .NotEqual(x => x.SourceDefinitionId)
            .WithMessage("Source and target definition must differ.");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
        RuleFor(x => x.StateMappings)
            .NotEmpty()
            .WithMessage("At least one state mapping is required.");
        RuleForEach(x => x.StateMappings).ChildRules(m =>
        {
            m.RuleFor(e => e.OldStateId).NotEmpty();
            m.RuleFor(e => e.NewStateId).NotEmpty();
        });
    }
}
