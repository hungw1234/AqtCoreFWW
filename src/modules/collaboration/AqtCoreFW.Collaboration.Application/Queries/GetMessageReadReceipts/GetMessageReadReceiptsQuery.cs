using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Collaboration.Application.Queries.GetMessageReadReceipts;

/// <summary>
/// Returns who has read a message ("seen by"). MessageId = T2 PublicId (Guid AK). RequesterId is the
/// caller (injected server-side from the JWT) — only a member of the message's conversation may read it.
/// </summary>
public sealed record GetMessageReadReceiptsQuery(
    Guid MessageId,
    Guid TenantId,
    Guid RequesterId) : IQuery<IReadOnlyList<MessageReadReceiptDto>>;
