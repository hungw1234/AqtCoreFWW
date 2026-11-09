using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateRtbfRequest;

/// <summary>
/// Creates a new RTBF anonymization request — PDPL Art. 17.
/// Resolves data subject by email via IIdentityModuleClient, then persists RtbfRequest.
/// Returns MessageId (Guid AK) — T3 plain-class, no PublicId.
/// </summary>
public sealed class CreateRtbfRequestCommandHandler(
    IRtbfRequestRepository repo,
    IIdentityModuleClient identityClient,
    IGuidGenerator guidGenerator) : IRequestHandler<CreateRtbfRequestCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateRtbfRequestCommand cmd, CancellationToken ct)
    {
        // Resolve data subject by email — tenant-scoped to prevent cross-tenant access [RT-05]
        var lookup = await identityClient.FindByEmailAsync(cmd.SubjectEmail, cmd.TenantId, ct);
        if (lookup is null)
            return Result.Fail(new ValidationError($"No user found with email '{cmd.SubjectEmail}'."));

        var rtbf = RtbfRequest.Create(
            guidGenerator,
            tenantId: lookup.TenantId ?? cmd.TenantId,
            dataSubjectId: lookup.UserId,
            subjectEmail: lookup.Email ?? cmd.SubjectEmail); // denormalized for search

        await repo.AddAsync(rtbf, ct);
        await repo.SaveChangesAsync(ct);

        return Result.Ok(rtbf.MessageId);
    }
}
