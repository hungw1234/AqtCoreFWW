using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.MasterData.Application.Commands.CreateExternalMapping;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateExternalMappingCommand(
    string InternalCode,
    string ExternalSystem,
    string ExternalCode,
    MappingDirection Direction,
    Guid? DictionaryId,
    DateTime ValidFrom,
    DateTime? ValidTo,
    Guid TenantId,
    Guid ActorId) : ICommand<Guid>;
