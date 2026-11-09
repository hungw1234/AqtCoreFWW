using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests.Helpers;

/// <summary>
/// Non-vacuous self-tests for <see cref="CrossSchemaSqlScanner"/>'s pattern coverage — proves the
/// read (FROM/JOIN) and write (UPDATE/INSERT INTO/MERGE) alternations actually fire, so a future
/// regex regression that silently stops detecting cross-schema access cannot pass unnoticed. The
/// sibling <c>ModuleIsolationTests.NoModule_Should_Reference_AnotherModuleSchema_InRawSql</c> asserts
/// the real src/modules tree is clean; these cases assert the detector itself is not dead.
///
/// Each case scans a throwaway temp module tree through the real <see cref="CrossSchemaSqlScanner.Scan"/>
/// entrypoint (not the private matcher), so the module-layout derivation, comment-skip, and whitelist
/// paths are exercised end-to-end.
/// </summary>
public sealed class CrossSchemaSqlScannerTests
{
    // Writes one synthetic module file at {temp}/{moduleDir}/{fileName} with the SQL on a non-comment
    // code line (so ScanLine does not skip it), scans via the real public API, then deletes the tree.
    private static IReadOnlyCollection<CrossSchemaSqlScanner.Violation> ScanSynthetic(
        string moduleDir, string fileName, string sqlLine)
    {
        var root = Path.Combine(Path.GetTempPath(), "xschema_scan_" + Guid.NewGuid().ToString("N"));
        try
        {
            var moduleFolder = Path.Combine(root, moduleDir);
            Directory.CreateDirectory(moduleFolder);
            File.WriteAllText(
                Path.Combine(moduleFolder, fileName),
                $"namespace Fake;\npublic static class Fake\n{{\n    public const string Sql = \"{sqlLine}\";\n}}\n");
            return CrossSchemaSqlScanner.Scan(root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_UpdateCrossSchema_Unbracketed_IsFlagged()
    {
        var violations = ScanSynthetic("forms", "FakeWrite.cs", "UPDATE audit.AuditLogEntries SET X = 1");

        violations.Should().ContainSingle().Which.ReferencedSchema.Should().Be("audit");
    }

    [Fact]
    public void Write_InsertIntoCrossSchema_Bracketed_IsFlagged()
    {
        var violations = ScanSynthetic("forms", "FakeInsert.cs", "INSERT INTO [identity].[Tenants] (Id) VALUES (1)");

        violations.Should().ContainSingle().Which.ReferencedSchema.Should().Be("identity");
    }

    [Fact]
    public void Write_InsertWithoutInto_Unbracketed_IsFlagged()
    {
        // T-SQL makes INTO optional; the guard must still catch a bare INSERT <schema>.<table>.
        var violations = ScanSynthetic("forms", "FakeBareInsert.cs", "INSERT identity.Tenants (Id) VALUES (1)");

        violations.Should().ContainSingle().Which.ReferencedSchema.Should().Be("identity");
    }

    [Fact]
    public void Write_MergeIntoCrossSchema_IsFlagged()
    {
        var violations = ScanSynthetic("forms", "FakeMerge.cs", "MERGE INTO config.Settings AS t USING src ON t.Id = src.Id");

        violations.Should().ContainSingle().Which.ReferencedSchema.Should().Be("config");
    }

    [Fact]
    public void Read_FromCrossSchema_StillFlagged()
    {
        // Regression guard: the write-keyword broadening must not break the original FROM/JOIN detection.
        var violations = ScanSynthetic("forms", "FakeRead.cs", "SELECT * FROM audit.AuditLogEntries");

        violations.Should().ContainSingle().Which.ReferencedSchema.Should().Be("audit");
    }

    [Fact]
    public void Write_OwnedSchema_IsNotFlagged()
    {
        var violations = ScanSynthetic("forms", "OwnedWrite.cs", "UPDATE forms.Submissions SET X = 1");

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Write_WhitelistedFile_IsNotFlagged()
    {
        // SeedOrphanPurger.cs → identity is a whitelisted (file, schema) pair; a cross-schema write
        // from it must stay exempt exactly like a cross-schema read would.
        var violations = ScanSynthetic("organization", "SeedOrphanPurger.cs", "UPDATE identity.AspNetUsers SET X = 1");

        violations.Should().BeEmpty();
    }
}
