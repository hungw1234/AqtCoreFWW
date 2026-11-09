namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// Async-local ambient tenant override for scopes that have no HttpContext — chiefly a
/// MassTransit consume scope (Slice 1D). <c>TenantScopeConsumeFilter</c> calls
/// <see cref="Push"/> with the tenant derived from the message payload; the scoped
/// <c>ITenantContext</c> factory reads <see cref="Current"/> and, when set, resolves a
/// <see cref="ScopedTenantContext"/> so downstream EF writes set SESSION_CONTEXT (RLS) and
/// honour the tenant global filters.
///
/// Registered as a singleton — the <see cref="AsyncLocal{T}"/> field carries per-async-flow
/// state, so a single instance safely isolates concurrent consumes. The value set inside a
/// <c>using</c> block flows to every async continuation spawned within it (the consumer body)
/// and is restored on dispose, so nested/sequential scopes do not leak across messages.
/// </summary>
public sealed class AmbientTenantScope
{
    private readonly AsyncLocal<TenantScopeState?> _current = new();

    /// <summary>The tenant pushed for the current async flow, or null when none is active.</summary>
    public TenantScopeState? Current => _current.Value;

    /// <summary>
    /// Pushes <paramref name="tenantId"/> as the ambient tenant for the current async flow.
    /// Dispose restores the previous value (supports nesting; the common case has no prior value).
    /// </summary>
    public IDisposable Push(Guid tenantId)
    {
        var previous = _current.Value;
        _current.Value = new TenantScopeState(tenantId);
        return new PopHandle(this, previous);
    }

    private sealed class PopHandle(AmbientTenantScope owner, TenantScopeState? previous) : IDisposable
    {
        private bool _popped;

        public void Dispose()
        {
            if (_popped) return;
            _popped = true;
            owner._current.Value = previous;
        }
    }
}

/// <summary>Immutable ambient tenant state carried by <see cref="AmbientTenantScope"/>.</summary>
public sealed record TenantScopeState(Guid TenantId);
