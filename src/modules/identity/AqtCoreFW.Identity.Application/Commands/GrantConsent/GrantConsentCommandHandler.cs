using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.GrantConsent;

public sealed class GrantConsentCommandHandler : IRequestHandler<GrantConsentCommand, Result<Guid>>
{
    private readonly IConsentRepository _consents;
    private readonly IGuidGenerator _guidGen;

    public GrantConsentCommandHandler(IConsentRepository consents, IGuidGenerator guidGen)
    {
        _consents = consents;
        _guidGen = guidGen;
    }

    public async Task<Result<Guid>> Handle(GrantConsentCommand cmd, CancellationToken ct)
    {
        // T2: Use domain factory — sets PublicId via IGuidGenerator
        var record = ConsentRecord.Create(
            _guidGen,
            dataSubjectId: cmd.UserId,
            purpose: cmd.PurposeCode,
            legalBasis: cmd.LegalBasis,
            dataSubjectType: cmd.DataSubjectType,
            evidenceJson: cmd.EvidenceJson,
            expiresAt: null,
            tenantId: cmd.TenantId,
            actorId: cmd.UserId);

        await _consents.AddAsync(record, ct);
        await _consents.SaveChangesAsync(ct);

        // T2: return PublicId (Guid AK) — the external addressing key
        return Result.Ok(record.PublicId);
    }
}
