using System.Security.Claims;
using Hangfire.Dashboard;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Restricts the Hangfire dashboard to the SystemAdmin role in EVERY environment (no dev bypass).
/// DisplayStorageConnectionString = false (set at the mount site) prevents connection-string leakage.
///
/// CSRF: the dashboard's mutating POSTs (requeue/delete/trigger) carry no antiforgery token; their only
/// CSRF defense is the hosting AuthServer's SameSite=Strict auth cookie — a cross-site forged POST can't
/// attach it. Relaxing that cookie to SameSite=Lax/None would expose these actions. Keep it Strict.
/// </summary>
public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        IsAuthorized(context.GetHttpContext().User);

    /// <summary>
    /// Pure authorization decision: only an authenticated SystemAdmin is authorized. Takes no environment
    /// input by design — env-independence is the hardening (there is no dev bypass). Public + static so the
    /// contract is directly unit-testable without spinning up Hangfire's DashboardContext.
    /// </summary>
    public static bool IsAuthorized(ClaimsPrincipal? user) => user?.IsInRole("SystemAdmin") == true;
}
