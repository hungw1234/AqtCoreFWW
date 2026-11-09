using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.UpdateDictionary;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateDictionaryCommand(
    Guid Id,
    string Name,
    string NameVi,
    string? Description,
    Guid ActorId) : ICommand;
