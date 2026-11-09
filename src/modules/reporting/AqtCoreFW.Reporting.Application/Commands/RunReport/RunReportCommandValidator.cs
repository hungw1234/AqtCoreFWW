using System.Text.Json;
using FluentValidation;

namespace AqtCoreFW.Reporting.Application.Commands.RunReport;

public sealed class RunReportCommandValidator : AbstractValidator<RunReportCommand>
{
    public RunReportCommandValidator()
    {
        RuleFor(x => x.ReportDefinitionId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.RequestedBy).NotEmpty();
        RuleFor(x => x.ParametersJson)
            .NotEmpty()
            .MaximumLength(4000)
            .Must(BeValidJson).WithMessage("ParametersJson must be valid JSON object.");
    }

    private static bool BeValidJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}
