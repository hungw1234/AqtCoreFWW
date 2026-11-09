using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.CreateReportDefinition;

public sealed class CreateReportDefinitionCommandValidator : AbstractValidator<CreateReportDefinitionCommand>
{
    public CreateReportDefinitionCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.SqlTemplate).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.ParametersSchema).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.OutputFormat).IsInEnum();
    }
}
