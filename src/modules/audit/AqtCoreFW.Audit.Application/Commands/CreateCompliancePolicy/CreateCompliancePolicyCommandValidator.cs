using System.Text.Json;
using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.CreateCompliancePolicy;

public sealed class CreateCompliancePolicyCommandValidator : AbstractValidator<CreateCompliancePolicyCommand>
{
    public CreateCompliancePolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Enforcement).IsInEnum();
        RuleFor(x => x.Rules)
            .NotEmpty()
            .Must(BeValidJson)
            .WithMessage("Rules must be valid JSON.");
    }

    private static bool BeValidJson(string rules)
    {
        if (string.IsNullOrWhiteSpace(rules)) return false;
        try
        {
            JsonDocument.Parse(rules);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
