using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Infrastructure.Persistence;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Persistence;

/// <summary>
/// One-shot fixture generator — captures legacy (no-prefix) AES-256-GCM ciphertexts
/// from the CURRENT unmodified EncryptedStringConverter BEFORE the v{N}: prefix change.
///
/// Run manually ONCE with:
///   dotnet test --filter "FullyQualifiedName~GenerateLegacyCipherFixtures" -- xunit.parallelizeTestCollections=false
///
/// Pipe output to Persistence/fixtures/legacy-cipher-fixtures.json.
/// DO NOT re-run after the converter is modified — the output would use the new format.
/// </summary>
public sealed class LegacyCipherFixtureGenerator
{
    // Stable 32-byte test key (base64: AAAA...): deterministic across runs.
    // This exact key is stored in the fixture so tests that load it can decrypt.
    private static readonly byte[] TestKey = new byte[32]; // all zeros

    private static readonly string[] Plaintexts =
    [
        "JBSWY3DPEHPK3PXP",                      // TotpSecret shape (base32-like)
        "Nguyễn Văn A",                            // UploaderDisplayName shape (multi-byte UTF-8)
        "",                                         // empty — should pass through as empty
        "Multi-line\nstring with\ttabs and 🔐 emoji" // mixed encoding
    ];

    [Fact(Skip = "manual-run-once — outputs fixture JSON to stdout, commit result to fixtures/")]
    public void GenerateLegacyCipherFixtures()
    {
        // Use EncryptedStringConverter via reflection to access its private static Encrypt method
        // so we stay tied to the exact production encrypt path.
        var converter = new EncryptedStringConverter(TestKey);
        var convertToProvider = converter.ConvertToProviderExpression.Compile();

        var entries = Plaintexts.Select(p => new
        {
            plaintext = p,
            cipher = convertToProvider(p)
        }).ToList();

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });

        // Output to stdout for capture
        Console.WriteLine("=== FIXTURE JSON START ===");
        Console.WriteLine(json);
        Console.WriteLine("=== FIXTURE JSON END ===");

        // Also write directly to fixture file (path relative from test output)
        var fixtureDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                "tests", "unit", "AqtCoreFW.Infrastructure.Extensions.Tests",
                "Persistence", "fixtures"));
        Directory.CreateDirectory(fixtureDir);

        var metadata = new
        {
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            test_key_base64 = Convert.ToBase64String(TestKey),
            note = "Captured from EncryptedStringConverter BEFORE v{N}: prefix addition. " +
                   "Used by Read_LegacyNoPrefix_DecryptsAsV1 test. DO NOT REGENERATE after converter change.",
            fixtures = entries
        };

        var fullJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(fixtureDir, "legacy-cipher-fixtures.json"), fullJson);

        // Assert something so xunit reports success
        entries.Should().HaveCount(4);
    }
}
