using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>Validates throughput query: date range must not exceed 1 year (365 days).</summary>
public sealed class GetThroughputByUserQueryValidator
    : AbstractValidator<GetThroughputByUserQuery>
{
    public const int MaxRangeDays = 365;

    public GetThroughputByUserQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.To - x.From).TotalDays <= MaxRangeDays)
            .WithName("DateRange")
            .WithMessage("Maximum date range is 1 year / Khoảng thời gian tối đa 1 năm.");

        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .WithMessage("From must not be after To.");
    }
}
