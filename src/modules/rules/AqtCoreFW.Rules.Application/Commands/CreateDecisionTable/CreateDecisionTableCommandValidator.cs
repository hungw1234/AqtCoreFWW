using FluentValidation;

namespace AqtCoreFW.Rules.Application.Commands.CreateDecisionTable;

public sealed class CreateDecisionTableCommandValidator : AbstractValidator<CreateDecisionTableCommand>
{
    public CreateDecisionTableCommandValidator()
    {
        RuleFor(x => x.RuleSetId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.InputColumns).NotEmpty();
        RuleFor(x => x.OutputColumns).NotEmpty();
        RuleFor(x => x.Rows).NotEmpty();
    }
}
