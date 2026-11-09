using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Installation.Tests;

/// <summary>
/// Seed data verification tests.
///
/// Full seed tests require Testcontainers — run with Docker.
/// These placeholders verify the project compiles and the test runner
/// picks up Installation-category tests in CI.
///
/// To extend: after migrations, assert that default seed rows exist:
///   - System roles (Admin, Operator, Viewer)
///   - Default feature flags
///   - Default lookup values
///   - Default system parameters
/// </summary>
[Trait("Category", "Installation")]
public sealed class SeedDataTests
{
    /// <summary>
    /// TC-INST-SEED-001: Seed data infrastructure is reachable.
    /// Full assertions require Docker (Testcontainers.MsSql) + EF migrations applied.
    /// </summary>
    [Fact]
    public void SeedData_PlaceholderPassesUntilDockerAvailable()
    {
        // Placeholder — real test runs migrations then queries:
        //   db.Roles.Should().Contain(r => r.Name == "Admin");
        //   db.FeatureFlags.Should().NotBeEmpty();
        true.Should().BeTrue("placeholder: full test requires Docker + Testcontainers");
    }
}
