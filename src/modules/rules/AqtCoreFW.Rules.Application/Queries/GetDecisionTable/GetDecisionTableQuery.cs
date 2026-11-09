using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Rules.Application.Queries.GetDecisionTable;

public sealed record GetDecisionTableQuery(long TableId, Guid TenantId) : IQuery<DecisionTableDto>;
