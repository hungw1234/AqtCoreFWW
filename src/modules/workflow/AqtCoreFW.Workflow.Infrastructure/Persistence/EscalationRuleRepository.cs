using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// Generic repository for EscalationRule — no custom queries needed beyond IRepository&lt;T,TId&gt;.
/// EscalationRule is T1 (long Id only, no PublicId) — addressed by long internal Id.
/// </summary>
public sealed class EscalationRuleRepository(WorkflowDbContext context)
    : GenericRepository<EscalationRule, long>(context);
