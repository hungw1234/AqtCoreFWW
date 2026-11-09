using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.GrantConsent;

/// <summary>Record data-processing consent per Law 91/2025/QH15 + Decree 356/2025 (PDPL).</summary>
public sealed record GrantConsentCommand(
    Guid UserId,
    Guid TenantId,
    string PurposeCode,
    string LegalBasis,
    string DataSubjectType,
    string? EvidenceJson = null) : IRequest<Result<Guid>>;
