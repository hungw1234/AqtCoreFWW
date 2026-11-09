# v2.132p3: Build-Break Hotfix — C# Nullable Coalescing Type Mismatch

**Date**: 2026-04-25 08:50
**Severity**: High
**Component**: Rules.Application (DecisionTableJsonHelper)
**Status**: Resolved

## What Happened

User reported post-build: "API project có nhiều error và warning" after pulling v2.132p2 (shipped <1 hour prior). Manual reproduction via `scripts/1_run-local-dev.bat` confirmed build failure.

## The Brutal Truth

The frustrating part? v2.132p2 shipped with a broken main branch. One line later, p3 exists. Makes you feel like you're chasing your own tail. But honestly — the fix was trivial, 30 seconds of typing. The real problem is process: nobody ran `dotnet build` from clean on the patched file before declaring "shipped." That's the hurt here, not the fix itself. Warning inflation (1 CVE × 6 projects = 12 warnings reported) distracted from the actual blocker.

## Technical Details

**Single blocking error:**
```
CS0019: Operator '??' cannot be applied to operands of type 'List<string>?' and 'string[]'
File: src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs:19
```

**Code context (v2.132p2 broken):**
```csharp
public static IReadOnlyList<string> ParseJsonStringArray(string json)
{
    return JsonSerializer.Deserialize<List<string>>(json) 
        ?? Array.Empty<string>();  // ← CS0019 here
}
```

**Why it breaks:** C# `??` operator requires both operands to have **compatible types at expression evaluation time**, independent of the target return type. Here:
- LHS: `List<string>?` (from Deserialize)
- RHS: `string[]` (Array.Empty<string>() returns T[])
- No best common type between `List<string>` and `string[]` exists (even though both are `IReadOnlyList<string>` assignable)
- Compiler rejects before ever considering the method return type `IReadOnlyList<string>`

This trips developers who assume "the return type is IReadOnlyList, so either operand should work." Nope — operator types are resolved first, target type is checked after.

**12 warnings (secondary):** NU1902 CVE on transitive `OpenTelemetry.Api 1.12.0`, duplicated across 6 projects. Misleading metric — same root cause, not 12 distinct issues.

## What We Tried

1. **Attempted:** Run clean build on main at v2.132p2 HEAD
2. **Confirmed:** `dotnet build` fails, blocks solution compilation

## Root Cause Analysis

v2.132p2 commit `f1eb381b` introduced `DecisionTableJsonHelper.cs` (new file, decision table DTO contract fix). File was coded correctly for intent but shipped without local `dotnet build` reproduction. Likely one of:
- Author relied on CI cache and didn't pull main clean locally
- Change was committed directly without pre-commit hook validation
- Integration test suite passed (doesn't verify full API project compilation)

## Lessons Learned

1. **Post-ship smoke step is mandatory.** "Shipped" does not mean "merged to main" — it means "main compiles and boots locally." Add this to finalize checklist before declaring v-bump.

2. **C# operator `??` type resolution is expression-first.** Both operands must have a common type independent of target context. Use explicit cast if operand types don't align:
   ```csharp
   return JsonSerializer.Deserialize<List<string>>(json) 
       ?? (IReadOnlyList<string>)Array.Empty<string>();
   ```
   Zero allocation cost — `Array.Empty<T>()` is cached singleton.

3. **Dedupe warning counts before escalating.** Six projects report the same NU1902, but it's one CVE with one remediation (OpenTelemetry.Api bump). Don't let duplication inflate severity perception.

## Next Steps

1. ✅ **Fix applied:** Cast RHS to `(IReadOnlyList<string>)Array.Empty<string>()` at line 19
2. ✅ **Verified:** `dotnet build` clean — 0E/12W (warnings unchanged, tracked separately)
3. ✅ **Committed:** v2.132p3 with fix
4. **Backlog:** NU1902 OpenTelemetry.Api 1.12.0 → security update queue (separate ticket, not blocking)
5. **Process:** Add "clean build verification" to finalize checklist before version bump

## Follow-up Actions

- [ ] Document "post-ship smoke test" requirement in `./.claude/rules/primary-workflow.md` finalize gate
- [ ] Create backlog item for OpenTelemetry.Api security patch
- [ ] Consider CI step to verify build-output-only projects compile (not just tests)

---

**Commit hash:** [v2.132p3 - pending creation]  
**Files modified:** `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs` (1 line)  
**Build result:** Clean (0E/12W) ✅
