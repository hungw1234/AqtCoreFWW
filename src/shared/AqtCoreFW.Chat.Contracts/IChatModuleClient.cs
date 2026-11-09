namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for chat/collaboration access.
/// Monolith: InProcessChatModuleClient (direct DB query, zero overhead).
/// Microservice: GrpcChatModuleClient (when module extracted).
/// </summary>
public interface IChatModuleClient
{
    Task<Guid> CreateContextualConversationAsync(Guid contextEntityId, string contextType, string name, Guid createdBy, Guid tenantId, CancellationToken ct = default);
}
