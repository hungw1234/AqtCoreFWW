using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.BulkImportUsers;

/// <summary>
/// Validates BulkImportUsersCommand — enforces InitialRole allowlist to prevent privilege escalation.
/// F-07: Only GovOfficer and Citizen are permitted as import roles.
/// </summary>
public sealed class BulkImportUsersCommandValidator : AbstractValidator<BulkImportUsersCommand>
{
    private static readonly string[] AllowedImportRoles = ["GovOfficer", "Citizen"];

    public BulkImportUsersCommandValidator()
    {
        // H4-BE-Arch: max row limit prevents DoS via oversized import payloads
        RuleFor(x => x.Rows)
            .NotEmpty().WithMessage("At least one row is required.")
            .Must(rows => rows.Count <= 500)
            .WithMessage("Bulk import is limited to 500 rows per request.");

        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
            row.RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
            row.RuleFor(r => r.InitialRole)
                .Must(r => r is null || AllowedImportRoles.Contains(r))
                .WithMessage($"InitialRole must be one of: {string.Join(", ", AllowedImportRoles)}.");
        });
    }
}
