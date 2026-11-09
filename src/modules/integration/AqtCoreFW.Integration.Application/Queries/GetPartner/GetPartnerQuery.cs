using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.GetPartner;

/// <summary>T2 Partner — PublicId is Guid (external AK).</summary>
public sealed record GetPartnerQuery(Guid PublicId, Guid TenantId) : IQuery<PartnerDto>;
