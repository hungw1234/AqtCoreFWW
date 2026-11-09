using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentValidation;

namespace AqtCoreFW.MasterData.Application.Commands.WorkflowTaxonomy;

// ── Create ────────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateWorkflowStateTaxonomyCommand(
    string Code,
    string NameVi,
    string NameEn,
    WorkflowStateCategory Category,
    int SortOrder = 0,
    string? I18nKey = null,
    string? Icon = null,
    string? Color = null) : ICommand<Guid>;

public sealed class CreateWorkflowStateTaxonomyCommandValidator
    : AbstractValidator<CreateWorkflowStateTaxonomyCommand>
{
    public CreateWorkflowStateTaxonomyCommandValidator()
    {
        // Mirrors plan §Validation Create — PascalCase convention, 1-64 chars
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z][A-Za-z0-9_]*$")
            .WithMessage("Code must start with a letter and contain only letters, digits, and underscores");
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.I18nKey).MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
        RuleFor(x => x.Color).MaximumLength(20);
        RuleFor(x => x.Category).IsInEnum();
    }
}

// ── Update ────────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateWorkflowStateTaxonomyCommand(
    Guid Id,
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    string? Color,
    WorkflowStateCategory Category,
    int SortOrder,
    byte[] RowVersion) : ICommand;

public sealed class UpdateWorkflowStateTaxonomyCommandValidator
    : AbstractValidator<UpdateWorkflowStateTaxonomyCommand>
{
    public UpdateWorkflowStateTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.I18nKey).MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
        RuleFor(x => x.Color).MaximumLength(20);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.RowVersion).NotNull().NotEmpty()
            .WithMessage("RowVersion is required for optimistic concurrency (R3)");
    }
}

// ── Delete (soft) ─────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteWorkflowStateTaxonomyCommand(
    Guid Id,
    string ConfirmCode,
    string Reason) : ICommand;

public sealed class DeleteWorkflowStateTaxonomyCommandValidator
    : AbstractValidator<DeleteWorkflowStateTaxonomyCommand>
{
    public DeleteWorkflowStateTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ConfirmCode).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500)
            .WithMessage("Reason must be at least 10 characters (plan §Validation Delete)");
    }
}

// ── Restore ───────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record RestoreWorkflowStateTaxonomyCommand(Guid Id) : ICommand;

public sealed class RestoreWorkflowStateTaxonomyCommandValidator
    : AbstractValidator<RestoreWorkflowStateTaxonomyCommand>
{
    public RestoreWorkflowStateTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
