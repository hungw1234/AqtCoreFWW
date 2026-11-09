using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Enums;

namespace AqtCoreFW.Integration.Application.Commands.UpdateMessageLogStatus;

[AuditCategory(AuditCategory.AppAccess)]
public sealed record UpdateMessageLogStatusCommand(
    Guid Id, MessageLogStatus NewStatus) : ICommand<MessageLogDto>;
