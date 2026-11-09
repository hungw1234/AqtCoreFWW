using System.Text.Json;
using AqtCoreFW.Forms.Application.Interfaces;
using NCalcExpression = NCalc.Expression;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Evaluates NCalc formula expressions against form field values.
///
/// DoS hardening (Forms launch-safety): NCalc has no native step/iteration/CPU guard, and a
/// CPU-bound expression cannot be cancelled cooperatively (a single expensive AST node never
/// observes a token). Racing the eval with a CancellationToken only makes the CALLER return —
/// the eval thread keeps running to completion. Under concurrency that abandons request
/// threadpool threads until the pool starves.
///
/// The guarantee here is <b>isolation, not abortion</b>: a process-wide <see cref="SemaphoreSlim"/>
/// caps how many evals may run at once. A malicious eval is abandoned (its thread runs on) but the
/// cap bounds total concurrent abandonment to <c>MaxConcurrentEvaluations</c> regardless of request
/// concurrency, so the request threadpool cannot be starved. The permit is released only when the
/// eval task actually finishes (release-on-completion) — releasing it when the caller times out
/// would defeat the cap. Callers that cannot acquire a permit, or whose eval exceeds the timeout,
/// get null (graceful degradation, same contract as before).
///
/// Expression length is capped at 500 chars (pre-gate). Custom function calls are blocked; only an
/// explicit allowlist of NCalc built-ins is permitted (fail-closed — a future NCalc built-in that
/// introduces unbounded iteration is denied by default rather than silently exposed).
/// </summary>
public sealed class FormulaEvaluationService : IFormulaEvaluationService
{
    private const int MaxExpressionLength = 500;

    // Per-eval wall-clock budget for the WaitAsync on the caller. This bounds the CALLER, not the
    // (uncancellable) eval — a malicious eval that blows past it is abandoned but stays capped by the
    // semaphore. The budget spans Task.Run scheduling latency + eval time, so it must absorb pool
    // cold-start / scheduling jitter, not just the ~sub-millisecond time a legit formula computes in;
    // 1s is generous for a warm production pool while still capping any pathological blow-up quickly.
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromMilliseconds(1000);

    // Short shed-fast window: under saturation, callers that cannot get a permit quickly return null
    // rather than queueing (which would itself pressure the request path).
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromMilliseconds(50);

    // Eval is CPU-bound; allowing more concurrent evals than cores yields no throughput and only
    // widens the abandonment blast radius. Cap at core count (floor 1). Static → process-wide cap,
    // independent of the AddScoped service lifetime.
    private static readonly int MaxConcurrentEvaluations = Math.Max(1, Environment.ProcessorCount);
    private static readonly SemaphoreSlim EvaluationGate = new(MaxConcurrentEvaluations, MaxConcurrentEvaluations);

    // Fail-closed allowlist of NCalc 6.1.1 built-ins (all finite, no unbounded iteration/recursion).
    // Any name not here is rejected by the EvaluateFunction handler. Case-insensitive because NCalc
    // may be configured for case-insensitive built-in matching.
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "Acos", "Asin", "Atan", "Atan2", "Ceiling", "Cos", "Exp", "Floor",
        "IEEERemainder", "Ln", "Log", "Log10", "Max", "Min", "Pow", "Round", "Sign",
        "Sin", "Sqrt", "Tan", "Truncate", "if", "ifs", "in"
    };

    public async Task<object?> EvaluateAsync(
        string formulaJson,
        IDictionary<string, object?> fieldValues,
        CancellationToken cancellationToken = default)
    {
        FormulaConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<FormulaConfig>(formulaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            return null; // malformed JSON — graceful degradation
        }

        if (config is null || string.IsNullOrWhiteSpace(config.Expression))
            return null;

        if (config.Expression.Length > MaxExpressionLength)
            return null;

        // Shed-fast: if the pool is saturated we cannot acquire quickly → degrade to null. A caller
        // cancellation while waiting for a permit also degrades to null (same graceful contract).
        try
        {
            if (!await EvaluationGate.WaitAsync(AcquireTimeout, cancellationToken).ConfigureAwait(false))
                return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        // The eval runs on a pool thread we may abandon. The permit is held until THIS task
        // completes (release-on-completion below), NOT until the caller stops awaiting — otherwise a
        // still-running abandoned eval would free its permit and the cap would be defeated.
        var evalTask = Task.Run(() => EvaluateCore(config.Expression, fieldValues), CancellationToken.None);
        _ = evalTask.ContinueWith(
            _ => EvaluationGate.Release(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await evalTask.WaitAsync(EvaluationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null; // eval exceeded its budget — abandoned but capped
        }
        catch (OperationCanceledException)
        {
            return null; // request cancelled while awaiting
        }
    }

    // Runs on the isolated eval thread. Any NCalc error degrades to null.
    private static object? EvaluateCore(string expression, IDictionary<string, object?> fieldValues)
    {
        try
        {
            var ncalc = new NCalcExpression(expression);

            // Bind field values via the parameter-resolution event (NOT the Parameters dictionary):
            // it fires for EVERY parameter the expression references, so a field the submission omits
            // — hidden by visibility rules, or an untouched optional the FE drops from the payload —
            // still resolves to 0 instead of raising "parameter not defined". This preserves the
            // pre-existing lenient contract (missing field ⇒ 0), which real forms depend on.
            ncalc.EvaluateParameter += (name, args) =>
            {
                args.Result = fieldValues.TryGetValue(name, out var val) ? val ?? 0 : 0;
            };

            // Fail-closed: only allowlisted built-ins pass. EvaluateFunction fires for names NCalc
            // does not resolve as a built-in; an allowlisted name that reaches here is still rejected
            // (it would only arrive if NCalc failed to resolve it), and any non-allowlisted call is
            // blocked outright.
            ncalc.EvaluateFunction += (name, args) =>
            {
                if (!AllowedFunctions.Contains(name))
                    throw new InvalidOperationException(
                        $"Function '{name}' is not permitted in formula expressions.");
            };

            return ncalc.Evaluate();
        }
        catch (Exception)
        {
            return null; // malformed expression, missing params, blocked function, etc.
        }
    }

    private sealed record FormulaConfig(string Expression, string? ResultType = null);
}
