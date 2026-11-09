using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.ModuleName.Application.Commands.CreateModuleName;

public sealed record CreateModuleNameCommand(
    Guid TenantId,
    string Title,
    string? Description,
    Guid CreatedBy) : ICommand<long>;
