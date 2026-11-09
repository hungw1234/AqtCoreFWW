using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// The introspection client secret must never silently fall back to the development value
/// (which is public in the repository) outside the local dev environments.
/// </summary>
public sealed class AuthResourceClientTests
{
    private static IConfiguration Config(string? secret = null, string? clientId = null)
    {
        var values = new Dictionary<string, string?>();
        if (secret is not null) values[AuthResourceClient.SecretKey] = secret;
        if (clientId is not null) values[AuthResourceClient.ClientIdKey] = clientId;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string DevSecret() => AuthResourceClient.ResolveSecret(Config(), "Development");

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Benchmark")]
    public void ResolveSecret_DevEnvironment_Missing_FallsBackToDevValue(string environment)
    {
        AuthResourceClient.ResolveSecret(Config(), environment).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Docker")]
    public void ResolveSecret_NonDevEnvironment_Missing_Throws(string environment)
    {
        var act = () => AuthResourceClient.ResolveSecret(Config(), environment);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{AuthResourceClient.SecretKey}*{environment}*");
    }

    [Fact]
    public void ResolveSecret_NonDevEnvironment_Whitespace_Throws()
    {
        var act = () => AuthResourceClient.ResolveSecret(Config("   "), "Production");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveSecret_NonDevEnvironment_DevValueConfigured_Throws()
    {
        var act = () => AuthResourceClient.ResolveSecret(Config(DevSecret()), "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*development value*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ResolveSecret_Configured_ReturnsConfiguredValue(string environment)
    {
        AuthResourceClient.ResolveSecret(Config("rotated-secret-from-vault"), environment)
            .Should().Be("rotated-secret-from-vault");
    }

    [Fact]
    public void ResolveClientId_DefaultsWhenMissing_UsesConfiguredOtherwise()
    {
        AuthResourceClient.ResolveClientId(Config()).Should().Be(AuthResourceClient.DefaultClientId);
        AuthResourceClient.ResolveClientId(Config(clientId: "custom-api")).Should().Be("custom-api");
    }

    [Fact]
    public void ValidateProductionSecrets_ProductionHostWithoutResourceClientSecret_Throws()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=prod;Database=X",
            ["Encryption:FieldKey"] = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=",
            ["Audit:HmacKey"] = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC=",
        });
        using var host = builder.Build();

        var act = () => host.ValidateProductionSecrets();
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{AuthResourceClient.SecretKey}*");
    }

    [Fact]
    public void ResolveSecret_NonDevEnvironment_DevValueWithTrailingNewline_Throws()
    {
        var act = () => AuthResourceClient.ResolveSecret(Config(DevSecret() + "\n"), "Production");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProductionSecretValidator_RejectsDevSecret()
    {
        var act = () => ProductionSecretValidator.AssertSecretValid(AuthResourceClient.SecretKey, DevSecret());
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{AuthResourceClient.SecretKey}*");
    }
}
