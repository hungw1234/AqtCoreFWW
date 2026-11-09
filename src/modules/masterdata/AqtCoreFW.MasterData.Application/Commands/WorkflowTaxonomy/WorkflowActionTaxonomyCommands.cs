using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentValidation;

namespace AqtCoreFW.MasterData.Application.Commands.WorkflowTaxonomy;

// ── Create ────────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateWorkflowActionTaxonomyCommand(
    string Code,
    string NameVi,
    string NameEn,
    int SortOrder = 0,
    string? I18nKey = null,
    string? Icon = null) : ICommand<Guid>;

public sealed class CreateWorkflowActionTaxonomyCommandValidator
    : AbstractValidator<CreateWorkflowActionTaxonomyCommand>
{
    public CreateWorkflowActionTaxonomyCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z][A-Za-z0-9_]*$")
            .WithMessage("Code must start with a letter and contain only letters, digits, and underscores");
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.I18nKey).MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}

// ── Update ────────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateWorkflowActionTaxonomyCommand(
    Guid Id,
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    int SortOrder,
    byte[] RowVersion) : ICommand;

public sealed class UpdateWorkflowActionTaxonomyCommandValidator
    : AbstractValidator<UpdateWorkflowActionTaxonomyCommand>
{
    public UpdateWorkflowActionTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.I18nKey).MaximumLength(100);
        RuleFor(x => x.Icon).MaximumLength(50);
        RuleFor(x => x.RowVersion).NotNull().NotEmpty()
            .WithMessage("RowVersion is required for optimistic concurrency (R3)");
    }
}

// ── Delete (soft) ─────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteWorkflowActionTaxonomyCommand(
    Guid Id,
    string ConfirmCode,
    string Reason) : ICommand;

public sealed class DeleteWorkflowActionTaxonomyCommandValidator
    : AbstractValidator<DeleteWorkflowActionTaxonomyCommand>
{
    public DeleteWorkflowActionTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ConfirmCode).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500);
    }
}

// ── Restore ───────────────────────────────────────────────────────────

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record RestoreWorkflowActionTaxonomyCommand(Guid Id) : ICommand;

public sealed class RestoreWorkflowActionTaxonomyCommandValidator
    : AbstractValidator<RestoreWorkflowActionTaxonomyCommand>
{
    public RestoreWorkflowActionTaxonomyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
