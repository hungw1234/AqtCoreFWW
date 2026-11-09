using System.Net;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Infrastructure.Middleware;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Loads enabled IpFilterRules from the DB and compiles them into an IpRuleSet
/// for IpFilterMiddleware evaluation.
///
/// Registered as Scoped in Identity.Infrastructure; middleware resolves it
/// via IServiceScopeFactory to respect EF DbContext lifetime.
///
/// CIDR validation: IPNetwork.TryParse (.NET 8+). Malformed rows (should not occur
/// after FluentValidation on save) are skipped with a warning log.
///
/// IpRuleSet stores CIDR strings (not IPNetwork objects) so it round-trips
/// through System.Text.Json for distributed cache serialization without custom converters.
/// </summary>
public sealed class IpRuleSetLoader(
    IIpFilterRuleRepository repository,
    ILogger<IpRuleSetLoader> logger) : IIpRuleSetLoader
{
    public async Task<IpRuleSet> LoadAsync(CancellationToken ct = default)
    {
        var rules = await repository.ListAllEnabledAsync(ct);

        var blockCidrs = new List<string>();
        var allowCidrs = new List<string>();

        foreach (var rule in rules)
        {
            // Validate CIDR at load-time to catch any data corruption.
            if (!IPNetwork.TryParse(rule.Cidr, out _))
            {
                logger.LogWarning(
                    "IpFilterRule {Id} has invalid CIDR '{Cidr}' — skipped during rule load.",
                    rule.Id, rule.Cidr);
                continue;
            }

            if (rule.IsBlock)
                blockCidrs.Add(rule.Cidr);
            else
                allowCidrs.Add(rule.Cidr);
        }

        return new IpRuleSet([.. blockCidrs], [.. allowCidrs]);
    }
}
