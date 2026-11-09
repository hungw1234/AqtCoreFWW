using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Ai.Application.Queries.GetModelProfiles;

/// <summary>Returns all active AI model profiles ordered by IsDefault desc, then Name.</summary>
public sealed record GetModelProfilesQuery : IQuery<IReadOnlyList<AiModelProfileDto>>;
