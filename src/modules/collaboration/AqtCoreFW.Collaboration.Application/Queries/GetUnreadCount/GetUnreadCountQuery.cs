using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Collaboration.Application.Queries.GetUnreadCount;

public sealed record GetUnreadCountQuery(
    Guid UserId,
    Guid TenantId) : IQuery<IReadOnlyList<UnreadCountDto>>;
