using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Rules.Infrastructure.Persistence;

/// <summary>
/// Rules module DB context — owns the "rules" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class RulesDbContext(DbContextOptions<RulesDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "rules";

    public DbSet<RuleSet> RuleSets { get; set; } = default!;
    public DbSet<Rule> Rules { get; set; } = default!;
    public DbSet<DecisionTable> DecisionTables { get; set; } = default!;
    public DbSet<RuleVersion> RuleVersions { get; set; } = default!;
    public DbSet<RuleTestCase> RuleTestCases { get; set; } = default!;
}
