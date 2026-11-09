using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.DeleteViewDefinition;

public sealed class DeleteViewDefinitionCommandValidator
    : AbstractValidator<DeleteViewDefinitionCommand>
{
    public DeleteViewDefinitionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");
    }
}
