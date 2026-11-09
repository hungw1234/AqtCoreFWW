using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.GetMessageLog;

public sealed record GetMessageLogQuery(Guid Id, Guid TenantId) : IQuery<MessageLogDto>;
