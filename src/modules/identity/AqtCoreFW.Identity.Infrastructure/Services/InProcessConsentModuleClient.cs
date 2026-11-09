using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// In-process implementation of IConsentModuleClient.
/// Directly creates ConsentRecord via repository — no MediatR cross-module hop.
/// Returns PublicId (Guid) — T2 external-facing AK.
/// </summary>
public sealed class InProcessConsentModuleClient(IConsentRepository consents, IGuidGenerator guidGen)
    : IConsentModuleClient
{
    public async Task<Guid> RecordConsentAsync(RecordConsentRequest request, CancellationToken ct)
    {
        var record = ConsentRecord.Create(
            guidGen,
            request.DataSubjectId,
            request.PurposeCode,
            request.LegalBasis,
            request.DataSubjectType,
            request.EvidenceJson,
            expiresAt: null,
            tenantId: request.TenantId,
            actorId: Guid.Empty); // system call — no user actor

        await consents.AddAsync(record, ct);
        await consents.SaveChangesAsync(ct);

        return record.PublicId;
    }
}
