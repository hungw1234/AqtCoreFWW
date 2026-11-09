using System.Collections.Concurrent;
using System.Reflection;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.SharedKernel.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior — auto-logs audit entries for all IBaseCommand executions.
/// Only logs on success; logs warning on audit failure but never breaks the main pipeline.
/// Best-effort resourceId extraction via cached reflection: reads <c>response.Value.Id</c>
/// when present (typical for Result&lt;TDto&gt; with an Id property), else passes null.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse>(
    IAuditLogger auditLogger,
    ILogger<AuditBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    // Cache one extractor per concrete response runtime type — built lazily on first seen.
    private static readonly ConcurrentDictionary<Type, Func<object?, string?>> _resourceIdExtractors = new();

    // Cache per-command-type suppress decision — avoids repeated reflection.
    private static readonly ConcurrentDictionary<Type, bool> _suppressCache = new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        // Commands decorated with [SuppressAutoAudit] emit their own structured audit entry.
        var isSuppressed = _suppressCache.GetOrAdd(
            typeof(TRequest),
            static t => t.GetCustomAttribute<SuppressAutoAuditAttribute>(inherit: true) is not null);
        if (isSuppressed) return response;

        // Only audit successful commands
        if (response is IResultBase rb && rb.IsFailed) return response;

        try
        {
            var typeName = typeof(TRequest).Name;
            var moduleName = ExtractModule(typeof(TRequest));
            var action = ExtractAction(typeName);
            var resourceType = ExtractResource(typeName);
            var resourceId = ExtractResourceId(response);

            // Pass typeof(TRequest) so AuditCategoryMapper can read [AuditCategoryAttribute]
            // off the command type with precedence over the verb-fallback.
            // dataSnapshot=null: auto-audit records action/resource only; structured evidence
            // is emitted by handlers decorated with [SuppressAutoAudit].
            await auditLogger.LogCommandAsync(action, moduleName, resourceType, resourceId,
                typeof(TRequest), dataSnapshot: null, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let audit logging crash the main pipeline. Reaching here now means DOUBLE failure —
            // the audit persist AND its durable retry-enqueue both died (AuditService recovers the common
            // single-failure case by enqueuing a Hangfire retry and returns normally). That is genuine
            // audit-event loss, so it is Critical, not a warning. The audit.ingestion.lost counter (raised
            // in AuditService) is the alert trigger; this log is the correlated forensic record.
            logger.LogCritical(ex,
                "Audit logging LOST for {CommandType} — sync persist and durable retry-enqueue both failed.",
                typeof(TRequest).Name);
        }

        return response;
    }

    // Extract best-effort resourceId from a Result<T>-shaped response whose Value has an Id property.
    // Returns null if the shape doesn't match — audit log will record null resourceId (acceptable).
    private static string? ExtractResourceId(object? response)
    {
        if (response is null) return null;
        var extractor = _resourceIdExtractors.GetOrAdd(response.GetType(), BuildExtractor);
        return extractor(response);
    }

    private static Func<object?, string?> BuildExtractor(Type responseType)
    {
        // Look for a "Value" property (FluentResults Result<T>.Value).
        var valueProp = responseType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueProp is null) return static _ => null;

        // Look for an "Id" property on the declared Value type (covers DTOs / records with Id).
        var idProp = valueProp.PropertyType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProp is null) return static _ => null;

        return response =>
        {
            if (response is null) return null;
            try
            {
                var value = valueProp.GetValue(response);
                if (value is null) return null;
                var id = idProp.GetValue(value);
                return id?.ToString();
            }
            catch
            {
                // ValueOrDefault semantics — any reflection failure degrades to null resourceId.
                return null;
            }
        };
    }

    private static string ExtractModule(Type type)
    {
        var ns = type.Namespace ?? "";
        var parts = ns.Split('.');
        // AqtCoreFW.{Module}.Application.Commands → Module
        return parts.Length >= 2 ? parts[1] : "Unknown";
    }

    // Shared verb list — both extractors must look at the same set or they diverge
    // (action column gets the verb, resource column gets the suffix; mismatch = useless audit rows).
    // Order matters only when a verb is a prefix of another (none currently); alphabetised for diff sanity.
    private static readonly string[] CommandVerbs = new[]
    {
        "Activate", "Approve", "Assign", "Close", "Create", "Deactivate", "Delegate", "Delete",
        "Ingest", "Lock", "Manage", "Mark", "Publish", "Register", "Reject", "Replace", "Report", "Reset",
        "Restore", "Revoke", "Send", "Submit", "Sync", "Trigger", "Unlock", "Update"
    };

    private static string ExtractAction(string typeName)
    {
        var name = typeName.Replace("Command", "");
        foreach (var v in CommandVerbs)
            if (name.StartsWith(v)) return v;
        return name.Length > 20 ? name[..20] : name;
    }

    private static string ExtractResource(string typeName)
    {
        var name = typeName.Replace("Command", "");
        foreach (var v in CommandVerbs)
            if (name.StartsWith(v)) return name[v.Length..];
        return name;
    }
}
