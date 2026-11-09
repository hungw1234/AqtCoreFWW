using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Ai.Application.Commands.CreateModelProfile;

/// <summary>Creates a new AI model profile configuration.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateModelProfileCommand(
    string Name,
    AiProvider Provider,
    string ModelId,
    string? Endpoint,
    int MaxTokens,
    decimal Temperature,
    decimal CostPerToken,
    bool IsDefault = false) : ICommand<AiModelProfileDto>;
