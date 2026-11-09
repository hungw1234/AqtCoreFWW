using AqtCoreFW.Audit.Application.Commands.UpdateClassificationRule;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Commands.UpdateClassificationRule;

public sealed class UpdateClassificationRuleCommandHandler(
    AuditDbContext dbContext,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateClassificationRuleCommand, Result>
{
    public async Task<Result> Handle(
        UpdateClassificationRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await dbContext.ClassificationRules
            .FirstOrDefaultAsync(r => r.Level == request.Level, cancellationToken);

        if (rule is null)
            return Result.Fail(new NotFoundError(
                $"ClassificationRule for level '{request.Level}' not found."));

        // Decode and validate concurrency token before mutating.
        byte[] caller;
        try { caller = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        {
            return Result.Fail(new BadRequestError(
                "RowVersion is not valid base64.",
                "CLASSIFICATION_RULE_BAD_ROWVERSION"));
        }

        if (caller.Length != 8)
            return Result.Fail(new BadRequestError(
                "RowVersion must be 8 bytes.",
                "CLASSIFICATION_RULE_BAD_ROWVERSION"));

        if (!caller.AsSpan().SequenceEqual(rule.RowVersion))
            return Result.Fail(new ConflictError("Stale row — refresh and retry."));

        rule.Update(
            request.MustEncryptAtRest,
            request.AllowExport,
            request.MaxRetentionDays,
            currentUser.UserName);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
