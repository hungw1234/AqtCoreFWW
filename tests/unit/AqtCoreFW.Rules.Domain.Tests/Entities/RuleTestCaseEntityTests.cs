using AqtCoreFW.Rules.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Rules.Domain.Tests.Entities;

public sealed class RuleTestCaseEntityTests
{
    // T1 FK: RuleSet.Id is long (internal PK). Use a constant to represent a parent FK.
    private const long RuleSetId = 1L;
    private static readonly Guid UserId = Guid.NewGuid();

    private static RuleTestCase CreateTestCase() =>
        RuleTestCase.Create(RuleSetId, "Age18Test", """{"Age":20}""", """{"IsAdult":true}""", UserId);

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var tc = CreateTestCase();
        tc.RuleSetId.Should().Be(RuleSetId);
        tc.Name.Should().Be("Age18Test");
        tc.InputData.Should().Be("""{"Age":20}""");
        tc.ExpectedOutput.Should().Be("""{"IsAdult":true}""");
        tc.LastRunResult.Should().BeNull();
        tc.LastRunAt.Should().BeNull();
    }

    [Fact]
    public void RecordResult_SetsResultAndTimestamp()
    {
        var tc = CreateTestCase();
        var timestamp = DateTime.UtcNow;
        tc.RecordResult("""{"IsSuccess":true}""", timestamp);
        tc.LastRunResult.Should().Be("""{"IsSuccess":true}""");
        tc.LastRunAt.Should().Be(timestamp);
    }

    [Fact]
    public void RecordResult_CalledTwice_OverwritesPreviousResult()
    {
        var tc = CreateTestCase();
        tc.RecordResult("first", DateTime.UtcNow.AddMinutes(-1));
        tc.RecordResult("second", DateTime.UtcNow);
        tc.LastRunResult.Should().Be("second");
    }

}
