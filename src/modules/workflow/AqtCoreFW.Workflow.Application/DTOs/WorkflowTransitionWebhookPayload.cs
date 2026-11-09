namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Outbound webhook payload fired on every workflow transition.
/// Sent via IWebhookService → Hangfire → HMAC-signed POST to subscriber endpoints.
/// NOTE: TenantId intentionally excluded (F06 — internal UUID must not leak to external systems).
/// DefinitionKey serves as public identifier instead.
/// Phase-4b B4c: InstanceId sourced from WorkflowInstance.PublicId (T2 AK).
///   DefinitionId sourced from WorkflowDefinition.PublicId (T2 AK).
///   FromStateId/NewStateInternalId: long (T1 WorkflowState.Id — no Guid AK on state).
/// </summary>
public sealed record WorkflowTransitionWebhookPayload(
    Guid InstanceId,
    Guid DefinitionId,
    string DefinitionKey,
    // arch:accept-long T1 WorkflowState: aggregate-internal id, FE/webhook consumers treat as opaque
    long FromStateId,
    // arch:accept-long T1 WorkflowState: aggregate-internal id, FE/webhook consumers treat as opaque
    long ToStateId,
    string ActionName,
    Guid TransitionedBy,
    DateTimeOffset Timestamp,
    string EntityType,
    Guid EntityId);
