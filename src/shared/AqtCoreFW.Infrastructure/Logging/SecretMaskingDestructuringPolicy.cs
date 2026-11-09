using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace AqtCoreFW.Infrastructure.Logging;

/// <summary>
/// Serilog destructuring policy that masks secret-shaped property values.
/// Prevents credential leakage in structured log output (QĐ 742 §4.4.a).
///
/// Matching strategy: suffix-anchored case-insensitive regex on property NAME.
/// Only the NAME drives masking — the value is replaced with "***".
///
/// Patterns masked (suffix-anchored, case-insensitive):
///   Password, Key, Token, Secret, Vault, HmacKey, FieldKey
///   Examples: "Password", "DbPassword", "VaultToken", "HmacKey", "FieldKey"
///
/// NOT masked (allowlist):
///   "KeyVersion" — ends with "Version", not "Key" (suffix anchor prevents false match)
///   "Username"   — does not match any suffix pattern
///   "VaultAddr"  — ends with "Addr", not a masked suffix
///
/// Note on regex: suffix anchor ($) is intentional — "VaultAddr" does NOT match
/// the "Vault" pattern because "Addr" follows; only keys ending in one of the
/// listed tokens are masked. This was a deliberate deviation from the spec's glob-style
/// pattern which would have incorrectly masked "VaultAddr".
/// </summary>
public sealed class SecretMaskingDestructuringPolicy : IDestructuringPolicy
{
    private const string MaskedValue = "***";

    // Suffix-anchored, case-insensitive. Matches property names ENDING with these tokens.
    // "Key$" matches "Key", "FieldKey", "HmacKey", "ApiKey" — but NOT "KeyVersion".
    // "Vault$" matches "Vault" — but NOT "VaultAddr" or "VaultToken" ... wait:
    //   "VaultToken" ends with "Token", so it IS masked via Token$.
    //   "VaultAddr"  does not end with any masked suffix — correctly NOT masked.
    private static readonly Regex SecretNamePattern = new(
        @"(Password|Key|Token|Secret|Vault|HmacKey|FieldKey)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeout: TimeSpan.FromMilliseconds(50));

    /// <inheritdoc/>
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        // This policy operates at the property level via the Serilog pipeline.
        // When Serilog destructures an anonymous/complex object, each property is passed through
        // registered policies. We intercept StructureValue to mask matching child properties.
        if (value is not null && IsComplexObject(value))
        {
            var masked = MaskStructure(value, propertyValueFactory);
            if (masked is not null)
            {
                result = masked;
                return true;
            }
        }

        result = null!;
        return false;
    }

    /// <summary>
    /// Returns true when the property name matches a secret-suffix pattern.
    /// Public for unit-test access; also used internally by <see cref="MaskStructure"/>.
    /// </summary>
    public static bool ShouldMaskKey(string propertyName)
        => SecretNamePattern.IsMatch(propertyName);

    /// <summary>
    /// Applies masking to all properties of a complex object.
    /// Returns a <see cref="StructureValue"/> with secret properties replaced by "***".
    /// Returns null if no masking was needed (caller skips override).
    /// </summary>
    private static StructureValue? MaskStructure(object value, ILogEventPropertyValueFactory factory)
    {
        var properties = value.GetType().GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        bool anyMasked = false;
        var logProperties = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            object? propValue;
            try { propValue = prop.GetValue(value); }
            catch { propValue = null; }

            LogEventPropertyValue logValue;

            if (ShouldMaskKey(prop.Name))
            {
                logValue = new ScalarValue(MaskedValue);
                anyMasked = true;
            }
            else
            {
                logValue = propValue is null
                    ? new ScalarValue(null)
                    : factory.CreatePropertyValue(propValue, destructureObjects: true);
            }

            logProperties.Add(new LogEventProperty(prop.Name, logValue));
        }

        return anyMasked ? new StructureValue(logProperties) : null;
    }

    private static bool IsComplexObject(object value)
    {
        var type = value.GetType();
        return !type.IsPrimitive
            && type != typeof(string)
            && type != typeof(decimal)
            && type != typeof(DateTime)
            && type != typeof(DateTimeOffset)
            && type != typeof(Guid)
            && !type.IsEnum;
    }
}
