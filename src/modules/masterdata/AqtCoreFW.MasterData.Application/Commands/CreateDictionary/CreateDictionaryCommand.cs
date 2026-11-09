using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.CreateDictionary;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateDictionaryCommand(
    string Code,
    string Name,
    string NameVi,
    string? Description,
    Guid TenantId,
    bool IsSystemDefined,
    Guid ActorId) : ICommand<Guid>;
