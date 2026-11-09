namespace AqtCoreFW.SystemParams.Application.DTOs;

/// <summary>Admin announcement DTO (full CRUD). Id is long (T1 bigint PK).</summary>
public sealed record AnnouncementDto(
    long Id,
    string Title,
    string Content,
    string Severity,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    bool IsActive,
    string? TargetRoles);

/// <summary>Public active announcement (cached, no IsActive field). Id is long (T1 bigint PK).</summary>
public sealed record ActiveAnnouncementDto(
    long Id,
    string Title,
    string Content,
    string Severity,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? TargetRoles);
