using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SystemParams.Application.DTOs;

namespace AqtCoreFW.SystemParams.Application.Queries;

/// <summary>Admin: get all announcements ordered by created date desc.</summary>
public sealed record GetAllAnnouncementsQuery() : IQuery<IReadOnlyList<AnnouncementDto>>;
