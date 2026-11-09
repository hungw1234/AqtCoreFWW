using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Self-service: promotes the caller's profile to be their default (named to match the domain method
/// <see cref="Domain.Entities.UserSignatureProfile.MarkDefault"/> — also keeps the audit verb extractor
/// on the "Mark" verb). The ≤1-default-per-user invariant is backed by a filtered unique index
/// <c>UX_UserSignatureProfile_Tenant_User_Default</c>. To avoid a transient two-defaults state that would
/// trip that index inside a single batch, the previous default is cleared and flushed FIRST, then the
/// target is marked and flushed. A crash between the two flushes leaves the user with zero defaults
/// (benign — they simply re-select), never two.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record MarkDefaultSignatureProfileCommand(
    Guid TenantId,
    Guid CallerUserId,
    Guid ProfilePublicId) : ICommand;

public sealed class MarkDefaultSignatureProfileCommandValidator : AbstractValidator<MarkDefaultSignatureProfileCommand>
{
    public MarkDefaultSignatureProfileCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.ProfilePublicId).NotEmpty();
    }
}

public sealed class MarkDefaultSignatureProfileCommandHandler(IUserSignatureProfileRepository repository)
    : IRequestHandler<MarkDefaultSignatureProfileCommand, Result>
{
    public async Task<Result> Handle(MarkDefaultSignatureProfileCommand cmd, CancellationToken ct)
    {
        var target = await repository.GetByPublicIdAsync(cmd.TenantId, cmd.CallerUserId, cmd.ProfilePublicId, ct);
        if (target is null)
            return Result.Fail(new NotFoundError($"Signature profile {cmd.ProfilePublicId} not found."));

        if (target.IsDefault)
            return Result.Ok(); // idempotent — already the default

        // Clear the existing default (if any, and a different profile) and flush BEFORE marking the new
        // one, so the filtered unique index never sees two IsDefault=true rows for the user.
        var current = await repository.GetDefaultAsync(cmd.TenantId, cmd.CallerUserId, ct);
        if (current is not null && current.PublicId != target.PublicId)
        {
            current.ClearDefault(cmd.CallerUserId);
            await repository.SaveChangesAsync(ct);
        }

        target.MarkDefault(cmd.CallerUserId);
        await repository.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
