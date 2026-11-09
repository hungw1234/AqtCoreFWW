using System.Reflection;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard for vocabulary-ownership: each top-level module class nested under
/// <see cref="PermCodes"/> (Identity, Files, Forms, …) MUST own exactly one MODULE namespace —
/// i.e. every permission-code value it declares shares the same first dotted segment.
///
/// This is the reflection form of "module A must not define module B's permission codes". After the
/// per-module file-split (one <c>PermCodes.&lt;Module&gt;.cs</c> per top-level class), this invariant
/// pins ownership to the file boundary: a code mis-filed into the wrong module's file (e.g. a HOSO.* code
/// pasted into the Forms class) turns the class's prefix set from a singleton into a pair and trips this guard.
/// NetArchTest works on assemblies/types, not files, so a nested-type→value-prefix reflection check is the
/// right tool for a same-assembly, per-file ownership rule.
/// </summary>
public sealed class PermCodeOwnershipConventionTests
{
    /// <summary>The first dotted segment (MODULE) of a MODULE.RESOURCE.ACTION code.</summary>
    private static string ModuleSegment(string code) => code.Split('.', 2)[0];

    /// <summary>Recursively collect every declared const value under a given class.</summary>
    private static List<string> CollectCodesUnder(Type root)
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

        Walk(root);
        return codes;
    }

    [Fact]
    public void Each_Top_Level_Module_Class_Owns_A_Single_Namespace_Prefix()
    {
        var violations = new List<string>();

        foreach (var moduleClass in typeof(PermCodes).GetNestedTypes(BindingFlags.Public))
        {
            var prefixes = CollectCodesUnder(moduleClass)
                .Select(ModuleSegment)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            // Classes with no codes (should not exist, but be defensive) are trivially compliant.
            if (prefixes.Count > 1)
                violations.Add($"{moduleClass.Name} → [{string.Join(", ", prefixes)}]");
        }

        violations.Should().BeEmpty(
            "each top-level PermCodes.<Module> class must declare codes for exactly one MODULE namespace " +
            "(the segment before the first dot) so the per-module file owns a single vocabulary. " +
            "A class spanning >1 prefix means a code is filed under the wrong module. Offenders: {0}",
            string.Join("; ", violations));
    }

    [Fact]
    public void Prefix_Extraction_Behaves_As_The_Guard_Assumes()
    {
        // Self-check: the ownership guard is only meaningful if ModuleSegment isolates the MODULE token.
        ModuleSegment("FORMS.SUBMIT.CREATE").Should().Be("FORMS");
        ModuleSegment("SECURITY.MFA_POLICY.VIEW").Should().Be("SECURITY");
        ModuleSegment("IDENTITY.SOD.VIEW_AUDIT_LOG").Should().Be("IDENTITY");
    }
}
