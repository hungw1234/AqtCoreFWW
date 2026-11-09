namespace AqtCoreFW.Collaboration.Application.Services;

/// <summary>
/// Abstraction for user presence tracking used by Application layer handlers.
/// Implemented by Infrastructure layer (IMemoryCache-backed for monolith).
/// </summary>
public interface IPresenceCacheService
{
    Task SetPresenceAsync(Guid userId, string status);
    Task<string?> GetPresenceAsync(Guid userId);
}
