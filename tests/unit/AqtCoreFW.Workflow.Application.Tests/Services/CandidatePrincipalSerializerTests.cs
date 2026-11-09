using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// H7: ONE canonical principal serializer shared by the resolver (P2 snapshot write)
/// and the inbox principal projection (P4 read). Guid → "D" lowercase, long → invariant
/// culture. Round-trip must be stable (deterministic snapshot for WorkflowTask.CandidateRefsJson).
/// </summary>
public sealed class CandidatePrincipalSerializerTests
{
    [Fact]
    public void Ref_Guid_IsLowercaseDFormat()
    {
        var g = Guid.Parse("AABBCCDD-EEFF-0011-2233-445566778899");

        var s = CandidatePrincipalSerializer.Ref(g);

        s.Should().Be("aabbccdd-eeff-0011-2233-445566778899");
    }

    [Fact]
    public void Ref_Long_IsInvariantCulture()
    {
        CandidatePrincipalSerializer.Ref(1234567890L).Should().Be("1234567890");
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsAsSet()
    {
        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "WORKFLOW.TASK.PROCESS", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "WORKFLOW.DEF.MANAGE", AssignmentCombineMode.Union),
        };

        var json = CandidatePrincipalSerializer.Serialize(predicates);
        var back = CandidatePrincipalSerializer.Deserialize(json);

        // Serialize canonicalizes order (M1) — round-trip preserves the SET, not input order.
        back.Should().BeEquivalentTo(predicates);
    }

    [Fact]
    public void Serialize_IsCanonical_OrderIndependent()
    {
        // M1: identical assignments resolved in different enumeration order (Dictionary.Values
        // has no contracted order) MUST produce the same wire string — P4 snapshot equality/audit.
        var a = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "Z.LAST", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "A.FIRST", AssignmentCombineMode.Union),
        };
        var b = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "A.FIRST", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "Z.LAST", AssignmentCombineMode.Union),
        };

        CandidatePrincipalSerializer.Serialize(a)
            .Should().Be(CandidatePrincipalSerializer.Serialize(b));
    }

    [Fact]
    public void Serialize_IsDeterministic_SameInputSameString()
    {
        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "A", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "B", AssignmentCombineMode.Union),
        };

        CandidatePrincipalSerializer.Serialize(predicates)
            .Should().Be(CandidatePrincipalSerializer.Serialize(predicates));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_NullOrBlank_ReturnsEmpty(string? json)
    {
        CandidatePrincipalSerializer.Deserialize(json).Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_EmptyArray_ReturnsEmpty()
    {
        CandidatePrincipalSerializer.Deserialize("[]").Should().BeEmpty();
    }
}
