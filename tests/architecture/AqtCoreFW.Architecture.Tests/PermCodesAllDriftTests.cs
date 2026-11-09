using System.Reflection;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard: the manually-curated <see cref="PermCodes.All"/> array MUST list every
/// permission-code constant declared anywhere in the <see cref="PermCodes"/> class tree — and nothing more.
///
/// Why it matters (a gap the existing PermCodesSeederCoverageTests does NOT close):
/// policies are bulk-registered from <c>PermCodes.All</c> (Identity ModuleRegistration) and permissions
/// are seeded from <c>PermCodes.All</c>. A new <c>const</c> added to a nested class but forgotten in the
/// <c>_all</c> array is therefore NEITHER policy-registered NOR seeded — yet an <c>[Authorize(Policy = …)]</c>
/// can still reference it, producing a runtime "policy not found" (500) / fail-closed 403. The seeder-coverage
/// test only iterates <c>_all</c>, so a const missing from <c>_all</c> slips past it. This guard closes that
/// declaration-site gap, and matters more after the per-module file-split spreads consts across many files.
/// </summary>
public sealed class PermCodesAllDriftTests
{
    /// <summary>Recursively collect every <c>public const string</c> value in the PermCodes class tree.</summary>
    private static List<string> CollectDeclaredCodes()
    {
        var codes = new List<string>();

        void Walk(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
                    codes.Add((string)field.GetRawConstantValue()!);

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                Walk(nested);
        }

        Walk(typeof(PermCodes));
        return codes;
    }

    [Fact]
    public void Every_Declared_Const_Must_Be_In_All()
    {
        var all = PermCodes.All.ToHashSet(StringComparer.Ordinal);

        var missing = CollectDeclaredCodes()
            .Where(code => !all.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "every PermCodes const must be listed in PermCodes.All — an unlisted const is not bulk-registered " +
            "as a policy nor seeded as a Permission row, so [Authorize(Policy = …)] on it fails closed. " +
            "Add it to the _all array. Missing: {0}",
            string.Join(", ", missing));
    }

    [Fact]
    public void All_Must_Not_Contain_Codes_Without_A_Declaring_Const()
    {
        var declared = CollectDeclaredCodes().ToHashSet(StringComparer.Ordinal);

        var orphaned = PermCodes.All
            .Where(code => !declared.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        orphaned.Should().BeEmpty(
            "every PermCodes.All entry must correspond to a declared const — an entry with no backing const " +
            "is a stale/typo value that cannot be referenced type-safely. Orphaned: {0}",
            string.Join(", ", orphaned));
    }

    [Fact]
    public void No_Permission_Code_Value_Is_Declared_Twice()
    {
        var duplicates = CollectDeclaredCodes()
            .GroupBy(c => c, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        duplicates.Should().BeEmpty(
            "the same permission-code string must be declared by exactly one const — a duplicate value " +
            "signals a copy-paste error where two resources unintentionally share one code. Duplicated: {0}",
            string.Join(", ", duplicates));
    }
}
