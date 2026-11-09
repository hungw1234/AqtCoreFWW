using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Files.Application.Queries.GetFileSigningSteps;

/// <summary>
/// Returns the ordered list of signing steps for an uploaded file.
/// The file tenant-ownership guard runs inside the handler before delegating to the Identity client.
/// </summary>
public sealed record GetFileSigningStepsQuery(
    Guid FileId,
    Guid TenantId)
    : IQuery<IReadOnlyList<SigningStepDto>>;
