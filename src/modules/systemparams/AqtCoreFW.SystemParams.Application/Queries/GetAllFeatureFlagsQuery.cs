using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Queries;

/// <summary>Get all feature flags (key prefix "feature:").</summary>
public sealed record GetAllFeatureFlagsQuery() : IQuery<IReadOnlyList<FeatureFlagDto>>;
