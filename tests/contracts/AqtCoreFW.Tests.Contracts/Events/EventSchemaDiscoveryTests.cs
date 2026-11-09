using System.Reflection;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Auto-discovery guard: ensures every IExternalDomainEvent type has a contract test.
/// When a new external event is added without a corresponding contract test, this fails.
/// </summary>
public sealed class EventSchemaDiscoveryTests
{
    /// <summary>
    /// Scans all loaded assemblies for IExternalDomainEvent implementations
    /// and verifies each has a serialization round-trip test in this project.
    /// </summary>
    [Fact]
    [Trait("Category", "Contract")]
    public void AllExternalDomainEvents_MustHaveContractTest()
    {
        // Load domain assemblies that may contain IExternalDomainEvent implementations
        var domainAssemblies = EventBearingAssemblies();

        var eventTypes = domainAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IExternalDomainEvent).IsAssignableFrom(t))
            .ToList();

        // Current known external events — update this list when adding new ones
        var testedTypes = new HashSet<string>
        {
            "NotificationSentEvent",
            // SharedKernel.Contracts outbox-routed events — see OutboxIntegrationEventsContractTests.
            "FormSubmissionAcknowledgedIntegrationEvent",
            "AuditLogAppendedIntegrationEvent",
            "WorkflowInstanceFinalizedIntegrationEvent",
        };

        foreach (var eventType in eventTypes)
        {
            testedTypes.Should().Contain(eventType.Name,
                $"IExternalDomainEvent '{eventType.FullName}' must have a contract test in this project. " +
                "Add a serialization round-trip test and update this set.");
        }
    }

    /// <summary>
    /// Verifies all IExternalDomainEvent types follow the IDs-only convention (no PII fields).
    /// Properties must be: Guid, string (for enum-like), DateTimeOffset, int, or bool.
    /// </summary>
    [Fact]
    [Trait("Category", "Contract")]
    public void AllExternalDomainEvents_MustContainOnlyIdFields()
    {
        var allowedTypes = new HashSet<Type>
        {
            typeof(Guid), typeof(string), typeof(DateTimeOffset),
            typeof(int), typeof(bool), typeof(long),
        };

        var eventTypes = EventBearingAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IExternalDomainEvent).IsAssignableFrom(t));

        foreach (var eventType in eventTypes)
        {
            var properties = eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                // Unwrap Nullable<T> so a nullable id field (e.g. AuditLogAppended.TenantId is Guid?)
                // is judged by its underlying value type, not by Nullable<Guid> which isn't allow-listed.
                var effectiveType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                allowedTypes.Should().Contain(effectiveType,
                    $"IExternalDomainEvent '{eventType.Name}.{prop.Name}' has type '{prop.PropertyType.Name}' " +
                    "which may contain PII. External events must use IDs only.");
            }
        }
    }

    /// <summary>
    /// Assemblies that may carry <see cref="IExternalDomainEvent"/> implementations. Includes
    /// SharedKernel.Contracts (Slice 1D) — the home of the outbox-routed integration events that
    /// are serialized to the outbox at rest, so their IDs-only invariant must be guarded here too.
    /// </summary>
    private static System.Reflection.Assembly[] EventBearingAssemblies() =>
    [
        typeof(AqtCoreFW.Notifications.Domain.Events.NotificationSentEvent).Assembly,
        typeof(AqtCoreFW.Files.Domain.Events.FileUploadedEvent).Assembly,
        typeof(AqtCoreFW.Workflow.Domain.Events.SlaBreachedEvent).Assembly,
        typeof(AqtCoreFW.SharedKernel.Contracts.Events.FormSubmissionAcknowledgedIntegrationEvent).Assembly,
    ];
}
