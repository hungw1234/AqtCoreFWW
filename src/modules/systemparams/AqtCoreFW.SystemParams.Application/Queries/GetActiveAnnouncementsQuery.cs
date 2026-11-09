using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Queries;

/// <summary>Public: get active announcements (cache-first, 1-min TTL).</summary>
public sealed record GetActiveAnnouncementsQuery() : IQuery<IReadOnlyList<ActiveAnnouncementDto>>;
