using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SystemParams.Domain.Entities;
using FluentValidation;

namespace AqtCoreFW.SystemParams.Application.Commands;

/// <summary>Create new system announcement. Returns long (T1 bigint PK, DB-assigned post-INSERT).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateAnnouncementCommand(
    string Title,
    string Content,
    AnnouncementSeverity Severity,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? TargetRoles) : ICommand<long>;

/// <summary>Validator for CreateAnnouncementCommand.</summary>
public sealed class CreateAnnouncementCommandValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Severity).IsInEnum();
        When(x => x.StartAt.HasValue && x.EndAt.HasValue, () =>
            RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt)
                .WithMessage("EndAt must be after StartAt."));
    }
}
