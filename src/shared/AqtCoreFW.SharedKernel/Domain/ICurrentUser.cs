namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>Resolved current user from JWT — injected in Application layer.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid? TenantId { get; }
    string UserName { get; }
    string[] Roles { get; }
    bool IsAuthenticated { get; }
}
