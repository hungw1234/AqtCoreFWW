using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Adapts IAttributeRuleRepository (domain interface) to IAttributeRuleReader (application interface).
/// Keeps Authorization handlers decoupled from repository infrastructure.
/// </summary>
public sealed class AttributeRuleReaderAdapter(IAttributeRuleRepository repository) : IAttributeRuleReader
{
    public async Task<List<AttributeRule>> GetRulesByAttributeAsync(
        string attributeKey, string attributeValue, CancellationToken ct = default)
    {
        var rules = await repository.GetByAttributeAsync(attributeKey, attributeValue, ct);
        return [.. rules];
    }
}
