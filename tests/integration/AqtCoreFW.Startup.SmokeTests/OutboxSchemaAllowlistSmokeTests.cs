using AqtCoreFW.Infrastructure.Persistence.Outbox;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Slice 1E step-7 — outbox dispatch allowlist regression guard.
///
/// The <see cref="OutboxDispatcher"/> only polls schemas registered via <c>AddOutboxSchema(...)</c>
/// (<see cref="IOutboxSchemaRegistry"/>). <c>OutboxWriter</c> guards its OWN write path loudly (it
/// throws on an unregistered schema), but the <c>OutboxInterceptor</c> capture path is registry-UNAWARE:
/// it persists an <c>OutboxMessages</c> row into the module DbContext's mapped schema regardless of the
/// allowlist. So an active outbox publisher whose schema is dropped from <c>AddOutboxSchema(...)</c> would
/// persist rows the dispatcher never polls — silent event loss, invisible until an integration event goes
/// missing downstream (the exact failure mode that makes audit-chain gaps undetectable after S2 extraction).
///
/// Resolves the REAL registry from the booted Api host (full <c>AddInfrastructure</c> + module wiring) and
/// asserts every active-publisher schema is present. Faithful — no hand-rolled mirror of the registration —
/// so dropping a real <c>AddOutboxSchema</c> call turns this red rather than passing on a stale copy.
///
/// Uses <see cref="ApiHostFixture"/> (own Testcontainer, IHostedService stripped) — the same host-DI seam
/// as <see cref="ApiControllerDiResolutionSmokeTests"/>; the singleton registry is fully wired at build.
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
[Trait("Category", "Smoke")]
public sealed class OutboxSchemaAllowlistSmokeTests : IClassFixture<ApiHostFixture>
{
    private readonly ApiHostFixture _fixture;

    public OutboxSchemaAllowlistSmokeTests(ApiHostFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Schemas with an active outbox publisher (Slice 1A/1B). Kept explicit — a deliberate contract:
    /// routing a new integration event through the outbox requires adding its module schema here, forcing
    /// a conscious "is this schema in the dispatch allowlist?" check. "forms" is registered centrally in
    /// <c>InfrastructureRegistration</c>; "audit" self-registers in <c>AuditInfrastructureRegistration</c>
    /// (AuditDbContext does not inherit ModuleDbContext).
    /// </summary>
    private static readonly IReadOnlyList<string> ActiveOutboxPublisherSchemas =
    [
        "forms", // FormSubmissionAcknowledged — SubmitForm / SubmitPublicForm
        "audit", // AuditLogAppended — AuditService
        "workflow", // WorkflowInstanceFinalized — ExecuteTransition final state (FormSubmission)
    ];

    [Fact, Trait("Category", "Smoke")]
    public void EveryActiveOutboxPublisherSchema_IsInTheDispatchAllowlist()
    {
        _fixture.CreateClient(); // force host build so AddOutboxSchema wiring has run

        using var scope = _fixture.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IOutboxSchemaRegistry>();

        var allowlist = registry.Schemas;
        allowlist.Should().NotBeEmpty("the outbox dispatcher must have at least the default module schemas registered");

        var missing = ActiveOutboxPublisherSchemas
            .Where(s => !allowlist.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();

        missing.Should().BeEmpty(
            "an active outbox publisher whose schema is missing from the AddOutboxSchema(...) allowlist " +
            "persists rows the dispatcher never polls — silent event loss. Missing: " + string.Join(", ", missing));
    }
}
