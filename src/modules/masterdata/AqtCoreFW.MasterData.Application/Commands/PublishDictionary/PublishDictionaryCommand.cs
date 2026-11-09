using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.PublishDictionary;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record PublishDictionaryCommand(
    Guid Id,
    string? Notes,
    Guid ActorId) : ICommand<int>;
