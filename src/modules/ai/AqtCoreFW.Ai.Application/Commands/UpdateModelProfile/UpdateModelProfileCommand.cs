using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Ai.Application.Commands.UpdateModelProfile;

/// <summary>Updates inference config and/or default/active flags of an existing profile. PublicId = T2 Guid AK external addressing.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateModelProfileCommand(
    Guid PublicId,
    int? MaxTokens,
    decimal? Temperature,
    bool? IsDefault,
    bool? IsActive) : ICommand<AiModelProfileDto>;
