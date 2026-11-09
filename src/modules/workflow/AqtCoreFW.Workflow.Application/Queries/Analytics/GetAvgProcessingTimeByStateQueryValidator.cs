using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>Validates processing-time query: date range must not exceed 1 year (365 days).</summary>
public sealed class GetAvgProcessingTimeByStateQueryValidator
    : AbstractValidator<GetAvgProcessingTimeByStateQuery>
{
    /// <summary>Maximum allowed date range in days for analytics queries.</summary>
    public const int MaxRangeDays = 365;

    public GetAvgProcessingTimeByStateQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.To - x.From).TotalDays <= MaxRangeDays)
            .WithName("DateRange")
            .WithMessage("Maximum date range is 1 year / Khoảng thời gian tối đa 1 năm.");

        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .WithMessage("From must not be after To.");
    }
}
