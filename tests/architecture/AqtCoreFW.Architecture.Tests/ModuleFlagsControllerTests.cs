using AqtCoreFW.Api;
using AqtCoreFW.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// The FE hides deferred-module routes/menus based on this endpoint's answer, so its mapping
/// from config to DTO must be exact: default (missing keys) reports everything enabled, and
/// each flag flips exactly its own field. Endpoint reachability with modules OFF is covered by
/// the host controller living in the Api assembly (never a removable Presentation part).
/// </summary>
public sealed class ModuleFlagsControllerTests
{
    private static ModuleFlagsDto Get(params (string Key, string Value)[] pairs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();
        var result = new ModuleFlagsController(config).Get();
        return (ModuleFlagsDto)((OkObjectResult)result).Value!;
    }

    [Fact]
    public void DefaultConfig_ReportsAllEnabled()
    {
        Get().Should().Be(new ModuleFlagsDto(true, true, true, true, true));
    }

    [Fact]
    public void EachFlagOff_FlipsExactlyItsOwnField()
    {
        Get(("Features:Ai:Enabled", "false"))
            .Should().Be(new ModuleFlagsDto(false, true, true, true, true));
        Get(("Features:Collaboration:Enabled", "false"))
            .Should().Be(new ModuleFlagsDto(true, false, true, true, true));
        Get(("Features:BusinessProcess:Enabled", "false"))
            .Should().Be(new ModuleFlagsDto(true, true, false, true, true));
        Get(("Features:Reporting:Enabled", "false"))
            .Should().Be(new ModuleFlagsDto(true, true, true, false, true));
        Get(("Features:Rules:Enabled", "false"))
            .Should().Be(new ModuleFlagsDto(true, true, true, true, false));
    }

    /// <summary>DTO field set must track DeferredModuleFlags.ModuleNames — adding a sixth module must fail here.</summary>
    [Fact]
    public void DtoFields_TrackDeclaredModuleNames()
    {
        var dtoFields = typeof(ModuleFlagsDto).GetProperties().Select(p => p.Name);
        dtoFields.Should().BeEquivalentTo(DeferredModuleFlags.ModuleNames);
    }
}
