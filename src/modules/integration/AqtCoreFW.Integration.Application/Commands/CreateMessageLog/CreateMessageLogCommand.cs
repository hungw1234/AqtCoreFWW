using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Enums;

namespace AqtCoreFW.Integration.Application.Commands.CreateMessageLog;

/// <summary>
/// Phase-07 PR-B: PartnerPublicId is Guid (T2 Partner external AK — required).
/// ContractPublicId is Guid? (T2 Contract external AK — optional).
/// Handler resolves both to internal long PKs via repository + tenant guard.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record CreateMessageLogCommand(
    Guid TenantId, Guid PartnerPublicId, Guid? ContractPublicId,
    MessageDirection Direction, string MessageType,
    string? Payload, string? CorrelationId) : ICommand<MessageLogDto>;
