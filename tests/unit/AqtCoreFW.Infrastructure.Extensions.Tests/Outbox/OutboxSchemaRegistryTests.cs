using AqtCoreFW.Infrastructure.Persistence.Outbox;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Outbox;

/// <summary>
/// Unit tests for <see cref="OutboxSchemaRegistry"/> and the
/// <see cref="OutboxSchemaRegistryExtensions.AddOutboxSchema"/> extension.
/// Registry doubles as the SQL-injection guard so name validation is a security test.
/// </summary>
public sealed class OutboxSchemaRegistryTests
{
    [Fact]
    public void Register_WithValidName_AddsLowercased()
    {
        var registry = new OutboxSchemaRegistry();

        registry.Register("Cases");

        registry.Schemas.Should().ContainSingle().Which.Should().Be("cases");
    }

    [Fact]
    public void Register_IsIdempotent()
    {
        var registry = new OutboxSchemaRegistry();

        registry.Register("workflow");
        registry.Register("workflow");
        registry.Register("WORKFLOW");

        registry.Schemas.Should().ContainSingle().Which.Should().Be("workflow");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cases; DROP TABLE x")]
    [InlineData("dbo.cases")]
    [InlineData("0cases")]                  // must start with letter
    [InlineData("__leading_underscore")]    // ditto
    [InlineData("name-with-dash")]          // dash not allowed
    [InlineData("a_very_long_schema_name_that_exceeds_thirty_characters_quite_definitely")]
    public void Register_WithUnsafeName_Throws(string bad)
    {
        var registry = new OutboxSchemaRegistry();

        var act = () => registry.Register(bad);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddOutboxSchema_RegistersSingletonAndAccumulates()
    {
        var services = new ServiceCollection();

        services.AddOutboxSchema("cases");
        services.AddOutboxSchema("workflow");
        services.AddOutboxSchema("workflow"); // idempotent

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IOutboxSchemaRegistry>();

        registry.Schemas.Should().BeEquivalentTo(new[] { "cases", "workflow" });

        // Same instance fetched twice — proves singleton lifetime.
        sp.GetRequiredService<IOutboxSchemaRegistry>()
            .Should().BeSameAs(registry);
    }
}
