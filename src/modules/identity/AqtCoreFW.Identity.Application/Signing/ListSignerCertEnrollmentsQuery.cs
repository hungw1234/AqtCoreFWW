using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>Lists a signer's certificate enrollments (active + revoked) within a tenant — admin view.</summary>
public sealed record ListSignerCertEnrollmentsQuery(Guid TenantId, Guid UserId)
    : IQuery<IReadOnlyList<SignerCertEnrollmentDto>>;

public sealed record SignerCertEnrollmentDto(
    long Id,
    Guid UserId,
    string SerialNumber,
    string Issuer,
    string Thumbprint,
    string? Subject,
    bool IsActive,
    DateTime EnrolledAt);

public sealed class ListSignerCertEnrollmentsQueryHandler(ISignerCertEnrollmentRepository repository)
    : IRequestHandler<ListSignerCertEnrollmentsQuery, Result<IReadOnlyList<SignerCertEnrollmentDto>>>
{
    public async Task<Result<IReadOnlyList<SignerCertEnrollmentDto>>> Handle(
        ListSignerCertEnrollmentsQuery query, CancellationToken ct)
    {
        var rows = await repository.GetByUserAsync(query.TenantId, query.UserId, ct);
        IReadOnlyList<SignerCertEnrollmentDto> dtos = rows
            .Select(e => new SignerCertEnrollmentDto(
                e.Id, e.UserId, e.SerialNumber, e.Issuer, e.Thumbprint, e.Subject, e.IsActive, e.EnrolledAt))
            .ToList();
        return Result.Ok(dtos);
    }
}
