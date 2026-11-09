using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Remote-readiness guard for the cross-module client seams (microservices completeness-matrix cell
/// "Sync inter-service + auth propagation"). Every <c>I*Client</c> interface under
/// <c>src/shared/*.Contracts</c> is an in-process seam today (<c>InProcess*Client</c>) that a later
/// strangler slice replaces with an HTTP <c>Remote*Client</c>. That swap is only possible while every
/// method signature stays <b>serialization-safe</b>: a parameter or return type that cannot cross a wire
/// (a <c>Stream</c>, an <c>IQueryable</c>, an EF entity, an <c>Expression</c>, a delegate…) silently
/// makes the seam un-remotable. This guard fails the build the moment such a type appears on a contract,
/// long before the remote client is written.
///
/// <para>
/// Discovery is a <b>hybrid</b> — source-text census over the <c>*.Contracts</c> folders decides which
/// assemblies actually declare a client interface, and reflection over exactly those assemblies inspects
/// the method shapes. Pure reflection would silently under-count: a brand-new <c>*.Contracts</c> assembly
/// that this test project does not reference is invisible to <see cref="Assembly.Load(string)"/>, so a
/// dirty contract in it would never be scanned. The census (a filesystem scan, not an assembly scan) sees
/// the new folder and forces a reference — mirroring why <see cref="AuditKeyedReaderAllowlistTests"/>
/// chose a source-text sweep. An empty <c>*.Contracts</c> scaffold (no client interface) maps to zero and
/// is never loaded.
/// </para>
/// <para>
/// Two deliberate carve-outs permit schema-less dynamic payloads that still cross a wire intact:
/// <c>object</c> as a collection value (the form-prefill value bag) and the System.Text.Json raw-JSON
/// carriers (<c>JsonDocument</c>/<c>JsonElement</c>, e.g. rule-engine input). A bare <c>object</c> is
/// still rejected.
/// </para>
/// </summary>
public sealed class CrossModuleClientContractSerializationSafetyTests
{
    /// <summary>
    /// The number of cross-module client interfaces expected across <c>src/shared/*.Contracts</c>
    /// (census 2026-07-15): 5 Identity + 3 Forms + 1 each for Workflow, SharedKernel(FormPrefill), Files,
    /// Consent, Chat, BusinessProcess, Audit, RuleEngine, OrgUnit, Notification. Pinned deliberately —
    /// a new cross-module seam is an architectural event that must be reviewed for remote-readiness and
    /// then bump this number. The brittleness is the review gate, not an accident.
    /// </summary>
    private const int ExpectedCrossModuleClientContractCount = 18;

    private static readonly Regex ClientInterfaceDeclaration =
        new(@"^\s*public\s+(?:sealed\s+|abstract\s+|partial\s+)*interface\s+(I\w*Client)\b",
            RegexOptions.Compiled);

    // Scalar leaf types that serialize as a single JSON/primitive value on any transport.
    private static readonly HashSet<Type> SafeScalarTypes =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string),
        typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(DateOnly), typeof(TimeOnly),
    ];

    // Generic collection shapes whose element/key/value types are recursed. A collection is safe iff
    // every type-argument is safe (as a collection argument — the carve-out for object applies there).
    private static readonly HashSet<Type> SafeCollectionDefinitions =
    [
        typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>),
        typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>),
        typeof(ISet<>), typeof(IReadOnlySet<>),
        typeof(List<>), typeof(HashSet<>),
        typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>), typeof(Dictionary<,>),
        typeof(KeyValuePair<,>),
    ];

    // Raw-JSON payload carriers — the System.Text.Json representation of dynamic JSON that crosses a
    // wire intact. The typed analog of the object-as-collection-value carve-out below: a contract may
    // legitimately accept schema-less JSON (e.g. IRuleEngineModuleClient's rule-evaluation input).
    private static readonly HashSet<Type> SafeRawJsonTypes =
    [
        typeof(JsonDocument), typeof(JsonElement),
    ];

    /// <summary>
    /// The invariant: every method on every cross-module client contract exposes only
    /// remote-serialization-safe parameter and return types.
    /// </summary>
    [Fact]
    public void AllCrossModuleClientContracts_ExposeOnlyRemoteSerializationSafeSignatures()
    {
        var repoRoot = FindRepoRoot();

        // ── Census: which *.Contracts assemblies declare which client interfaces (source-text) ──
        var census = CensusClientInterfacesBySourceText(repoRoot);
        var censusCount = census.Values.Sum(names => names.Count);

        censusCount.Should().Be(ExpectedCrossModuleClientContractCount,
            "the source-text census of I*Client interfaces under src/shared/*.Contracts must match the " +
            "pinned count; a mismatch means a cross-module seam was added or removed without review " +
            "(update ExpectedCrossModuleClientContractCount and confirm the new seam is remote-ready)");

        // ── Reflection: load exactly the assemblies the census found a client in ──
        var reflectedInterfaces = LoadClientInterfaces(census.Keys);
        var reflectedNames = reflectedInterfaces.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var censusNames = census.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);

        reflectedNames.Should().BeEquivalentTo(censusNames,
            "reflection must see exactly the client interfaces the filesystem census found — a difference " +
            "means an assembly failed to load (missing ProjectReference) or the discovery filters diverged");
        reflectedInterfaces.Count.Should().Be(ExpectedCrossModuleClientContractCount,
            "the reflected client-interface set must match the pinned count (undercount would let a dirty " +
            "contract escape the scan)");

        // ── Inspect every method signature ──
        var violations = InspectContractMethods(reflectedInterfaces);

        violations.Should().BeEmpty(
            "every cross-module client contract must stay remote-serialization-safe so its InProcess*Client " +
            "seam can later be swapped for an HTTP Remote*Client; the types below cannot cross a wire:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Mutation-proof for the leaf type checker: proves <see cref="IsRemoteSafe"/> actually discriminates
    /// wire-safe types from wire-breaking ones, so the main guard is not vacuously green.
    /// </summary>
    [Fact]
    public void IsRemoteSafe_DiscriminatesSafeFromWireBreakingTypes()
    {
        // SAFE
        AssertSafe(typeof(Guid));
        AssertSafe(typeof(string));
        AssertSafe(typeof(bool));
        AssertSafe(typeof(int?));
        AssertSafe(typeof(DateTimeOffset));
        AssertSafe(typeof(byte[]));                                  // array of a safe scalar
        AssertSafe(typeof(DayOfWeek));                               // enum
        AssertSafe(typeof(IReadOnlyList<Guid>));
        AssertSafe(typeof(IReadOnlyDictionary<string, object?>));    // object carve-out (collection value)
        AssertSafe(typeof(JsonDocument));                            // raw JSON payload carve-out
        AssertSafe(typeof(JsonElement));                             // raw JSON payload carve-out
        AssertSafe(typeof(Task));                                    // no payload
        AssertSafe(typeof(Task<bool>));
        AssertSafe(typeof(AqtCoreFW.SharedKernel.Contracts.Clients.OrgUnitInfoDto)); // real *.Contracts DTO

        // UNSAFE
        AssertUnsafe(typeof(IQueryable<int>));
        AssertUnsafe(typeof(System.IO.Stream));
        AssertUnsafe(typeof(Expression<Func<int, bool>>));
        AssertUnsafe(typeof(Action));
        AssertUnsafe(typeof(Func<int, int>));
        AssertUnsafe(typeof(Type));
        AssertUnsafe(typeof(IntPtr));
        AssertUnsafe(typeof(object));                                // bare object (not a collection value)
        AssertUnsafe(typeof(SyntheticNonContractDto));              // POCO defined outside *.Contracts
    }

    /// <summary>
    /// Mutation-proof for the method inspector (not just the leaf checker): a synthetic dirty seam run
    /// through the exact production inspection path must surface violations. If a real contract ever grew
    /// a dirty member, this demonstrates the main guard would go red.
    /// </summary>
    [Fact]
    public void MethodInspector_FlagsDirtyContractSeam()
    {
        var violations = InspectContractMethods([typeof(IDirtySeam)]);

        violations.Should().Contain(v => v.Contains("QueryAsync", StringComparison.Ordinal),
            "an IQueryable return must be flagged");
        violations.Should().Contain(v => v.Contains("Open", StringComparison.Ordinal),
            "a Stream return must be flagged");
    }

    /// <summary>
    /// Pins the object carve-out (design decision): bare <c>object</c> is rejected, but <c>object</c> as a
    /// dictionary/collection value (the JSON value-bag of <c>IFormPrefillContextClient</c>) is allowed.
    /// Guards the decision against silent drift in either direction.
    /// </summary>
    [Fact]
    public void BareObject_IsRejected_ButObjectAsCollectionValue_IsAllowed()
    {
        IsRemoteSafe(typeof(object), asCollectionArg: false, [], out _)
            .Should().BeFalse("a bare object parameter/return carries no schema and cannot be a typed contract");
        IsRemoteSafe(typeof(object), asCollectionArg: true, [], out _)
            .Should().BeTrue("object is permitted only as a collection value — the form-prefill value bag");
        IsRemoteSafe(typeof(IReadOnlyDictionary<string, object?>), asCollectionArg: false, [], out _)
            .Should().BeTrue("a dictionary with an object value is the sanctioned value-bag shape");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Discovery
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>src/shared/*.Contracts</c> folder's non-comment source for
    /// <c>public interface I*Client</c> declarations. Returns a map of assembly simple-name → the client
    /// interface names it declares. Assemblies with no client interface (empty scaffolds) are omitted.
    /// </summary>
    private static Dictionary<string, HashSet<string>> CensusClientInterfacesBySourceText(string repoRoot)
    {
        var sharedRoot = Path.Combine(repoRoot, "src", "shared");
        Directory.Exists(sharedRoot).Should().BeTrue($"src/shared must exist under {repoRoot}");

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var contractsDir in Directory.EnumerateDirectories(sharedRoot, "*.Contracts"))
        {
            var assemblyName = Path.GetFileName(contractsDir);
            var found = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(contractsDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGeneratedPath(file)) continue;

                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = rawLine.TrimStart();
                    if (line.StartsWith("//", StringComparison.Ordinal)) continue; // also excludes /// XML-doc
                    var match = ClientInterfaceDeclaration.Match(rawLine);
                    if (match.Success) found.Add(match.Groups[1].Value);
                }
            }

            if (found.Count > 0) result[assemblyName] = found;
        }

        return result;
    }

    /// <summary>
    /// Loads each census-identified assembly by simple name and returns its public <c>I*Client</c>
    /// interfaces. A load failure means the census saw a folder the test project does not reference —
    /// the assertion tells the maintainer to add the ProjectReference.
    /// </summary>
    private static List<Type> LoadClientInterfaces(IEnumerable<string> assemblyNames)
    {
        var interfaces = new List<Type>();

        foreach (var name in assemblyNames)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(name);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"'{name}' declares a cross-module client interface but could not be loaded " +
                    $"({ex.GetType().Name}). Add a ProjectReference to it in " +
                    "AqtCoreFW.Architecture.Tests.csproj so the serialization-safety guard can inspect it.");
            }

            interfaces.AddRange(
                SafeGetTypes(assembly)
                    .Where(t => t.IsInterface && t.IsPublic && Regex.IsMatch(t.Name, @"^I\w*Client$")));
        }

        return interfaces;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Method inspection
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For each contract interface, checks every method's return type and every parameter (except
    /// <see cref="CancellationToken"/>) against <see cref="IsRemoteSafe"/>. Methods of base contract
    /// interfaces (also in a <c>*.Contracts</c> assembly) are included; system interfaces are skipped.
    /// </summary>
    private static List<string> InspectContractMethods(IEnumerable<Type> contracts)
    {
        var violations = new List<string>();

        foreach (var contract in contracts)
        {
            var methods = contract.GetMethods()
                .Concat(contract.GetInterfaces()
                    .Where(i => i.Assembly.GetName().Name?.EndsWith(".Contracts", StringComparison.Ordinal) == true)
                    .SelectMany(i => i.GetMethods()));

            foreach (var method in methods.Distinct())
            {
                if (!IsRemoteSafe(method.ReturnType, asCollectionArg: false, [], out var returnReason))
                    violations.Add($"{contract.Name}.{method.Name}: return '{Describe(method.ReturnType)}' — {returnReason}");

                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(CancellationToken)) continue;
                    if (!IsRemoteSafe(parameter.ParameterType, asCollectionArg: false, [], out var paramReason))
                        violations.Add(
                            $"{contract.Name}.{method.Name}: parameter '{parameter.Name}: " +
                            $"{Describe(parameter.ParameterType)}' — {paramReason}");
                }
            }
        }

        return violations;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Type-safety checker (default-deny)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true iff <paramref name="type"/> can cross a service boundary as data. Default-deny:
    /// anything not provably a scalar, enum, safe collection, or a DTO defined in a <c>*.Contracts</c>
    /// assembly is rejected. <paramref name="asCollectionArg"/> relaxes the check to allow <c>object</c>
    /// as a collection value only. <paramref name="visited"/> guards against self-referential DTOs.
    /// </summary>
    private static bool IsRemoteSafe(Type type, bool asCollectionArg, HashSet<Type> visited, out string? reason)
    {
        reason = null;

        // ── Unwrap task / nullable / array before any classification ──
        if (type == typeof(Task) || type == typeof(ValueTask))
            return true; // completes with no payload
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                return IsRemoteSafe(type.GetGenericArguments()[0], asCollectionArg: false, visited, out reason);
        }
        var underlyingNullable = Nullable.GetUnderlyingType(type);
        if (underlyingNullable is not null)
            return IsRemoteSafe(underlyingNullable, asCollectionArg, visited, out reason);
        if (type.IsArray)
            return IsRemoteSafe(type.GetElementType()!, asCollectionArg: true, visited, out reason);

        // ── Explicit denylist (better messages than the catch-all default-deny) ──
        if (typeof(System.IO.Stream).IsAssignableFrom(type)) { reason = "Stream is a byte pipe, not serializable data"; return false; }
        if (typeof(IQueryable).IsAssignableFrom(type)) { reason = "IQueryable is a server-side query, not a materialized result"; return false; }
        if (typeof(Expression).IsAssignableFrom(type)) { reason = "Expression trees do not cross a service boundary"; return false; }
        if (typeof(Delegate).IsAssignableFrom(type)) { reason = "delegates/Func/Action are not serializable"; return false; }
        if (type == typeof(Type) || typeof(MemberInfo).IsAssignableFrom(type)) { reason = "reflection metadata is not remote-safe"; return false; }
        if (type == typeof(IntPtr) || type == typeof(UIntPtr)) { reason = "pointers are not remote-safe"; return false; }
        if (typeof(DbContext).IsAssignableFrom(type)) { reason = "DbContext must not cross a module boundary"; return false; }
        if (type.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true)
        { reason = "domain type on a contract; expose a DTO defined in a *.Contracts assembly instead"; return false; }
        if (type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true)
        { reason = "ASP.NET transport type (IActionResult…) is not part of the contract surface"; return false; }

        // ── Allowlist ──
        if (SafeScalarTypes.Contains(type)) return true;
        if (type.IsEnum) return true;
        if (SafeRawJsonTypes.Contains(type)) return true; // dynamic JSON payload carve-out
        if (type == typeof(object))
        {
            if (asCollectionArg) return true;
            reason = "a bare object carries no schema; object is permitted only as a collection value";
            return false;
        }
        if (type.IsGenericType && SafeCollectionDefinitions.Contains(type.GetGenericTypeDefinition()))
        {
            foreach (var argument in type.GetGenericArguments())
                if (!IsRemoteSafe(argument, asCollectionArg: true, visited, out reason))
                    return false;
            return true;
        }
        if (IsContractPoco(type))
        {
            if (!visited.Add(type)) return true; // cycle guard — a type already on the stack is fine
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead))
                if (!IsRemoteSafe(property.PropertyType, asCollectionArg: false, visited, out reason))
                {
                    reason = $"property '{type.Name}.{property.Name}': {reason}";
                    return false;
                }
            // Public fields carry data across a wire just like properties — check them too so the
            // default-deny promise holds for a future field-based DTO, not only property/record ones.
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!IsRemoteSafe(field.FieldType, asCollectionArg: false, visited, out reason))
                {
                    reason = $"field '{type.Name}.{field.Name}': {reason}";
                    return false;
                }
            return true;
        }

        // ── Default deny ──
        reason = $"'{Describe(type)}' is not a scalar, enum, safe collection, or a DTO defined in a *.Contracts assembly";
        return false;
    }

    /// <summary>A plain data class/struct authored in a <c>*.Contracts</c> assembly.</summary>
    private static bool IsContractPoco(Type type) =>
        (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum)) &&
        type.Assembly.GetName().Name?.EndsWith(".Contracts", StringComparison.Ordinal) == true;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static void AssertSafe(Type type) =>
        IsRemoteSafe(type, asCollectionArg: false, [], out var reason)
            .Should().BeTrue($"{Describe(type)} should be remote-safe but was rejected: {reason}");

    private static void AssertUnsafe(Type type) =>
        IsRemoteSafe(type, asCollectionArg: false, [], out _)
            .Should().BeFalse($"{Describe(type)} should be rejected as not remote-safe");

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
    }

    private static string Describe(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(Describe));
        return $"{name}<{args}>";
    }

    /// <summary>
    /// Walks up from the test binary until it finds a directory containing both <c>src/</c> and
    /// <c>tests/</c>. Mirrors <see cref="AuditKeyedReaderAllowlistTests"/>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            $"could not locate repo root walking up from {AppContext.BaseDirectory} (expected src/ + tests/)");
        return dir!.FullName;
    }

    // ── Synthetic types for the mutation-proof tests (deliberately NOT in a *.Contracts assembly) ──

    private sealed class SyntheticNonContractDto
    {
        public Guid Id { get; init; }
    }

    private interface IDirtySeam
    {
        Task<IQueryable<int>> QueryAsync();
        System.IO.Stream Open();
    }
}
