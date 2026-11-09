using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// ReAct (Reason-Act) agent backed by Ollama.
/// Loop: LLM reasons → requests tool → tool executes → result fed back → repeat (max 5).
/// Tool calls parsed from LLM output as JSON: {"tool": "name", "args": {...}}.
/// Available tools: classify_text, summarize (read-only text operations, no SQL).
/// Error codes: GOV_AI_002 (Ollama unavailable), GOV_AI_AGENT_MAX_ITER (exceeded iterations).
/// </summary>
public sealed class OllamaReActAgent(
    IChatClient chatClient,
    ILogger<OllamaReActAgent> logger) : IAiAgent
{
    private const int MaxIterations = 5;

    private static readonly string SystemPrompt = """
        You are a government document processing assistant with access to the following tools:

        - classify_text: Classify text sensitivity level. Args: {"text": "..."}
        - summarize: Summarize a long text. Args: {"text": "...", "max_words": "100"}

        To use a tool, respond ONLY with JSON in this exact format:
        {"tool": "tool_name", "args": {"key": "value"}}

        When you have the final answer, respond with plain text (not JSON).
        Think step by step. Use tools when needed to complete the task.
        """;

    public async Task<Result<AgentResponse>> ExecuteAsync(
        AgentTask task, CancellationToken ct = default)
    {
        if (task is null)
            return Result.Ok(new AgentResponse(
                Output: "GOV_AI_AGENT: Task cannot be null.",
                Metadata: new Dictionary<string, string>(),
                Success: false));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildInitialPrompt(task))
        };

        var metadata = new Dictionary<string, string>
        {
            ["taskType"] = task.TaskType,
            ["tenantId"] = task.TenantId.ToString()
        };

        try
        {
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                logger.LogDebug("OllamaReActAgent: iteration={Iter} tenant={TenantId} taskType={TaskType}",
                    iteration, task.TenantId, task.TaskType);

                var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
                var text = response.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Try to parse as tool call
                var toolCall = TryParseToolCall(text);
                if (toolCall is null)
                {
                    // No tool call → final answer
                    metadata["iterations"] = (iteration + 1).ToString();
                    return Result.Ok(new AgentResponse(text, metadata, Success: true));
                }

                // Execute tool (read-only)
                var toolResult = await ExecuteToolAsync(toolCall.Value.tool, toolCall.Value.args, task.TenantId, ct);

                logger.LogDebug("OllamaReActAgent: tool={Tool} result={Result}",
                    toolCall.Value.tool, Truncate(toolResult, 100));

                // Append assistant message + tool result for next iteration
                messages.Add(new ChatMessage(ChatRole.Assistant, text));
                messages.Add(new ChatMessage(ChatRole.User,
                    $"Tool '{toolCall.Value.tool}' result: {toolResult}"));
            }

            // Exceeded max iterations — return last LLM output as partial answer
            metadata["iterations"] = MaxIterations.ToString();
            metadata["error"] = "GOV_AI_AGENT_MAX_ITER";

            return Result.Ok(new AgentResponse(
                Output: "GOV_AI_AGENT_MAX_ITER: Agent exceeded maximum iterations without reaching final answer.",
                Metadata: metadata,
                Success: false));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("OllamaReActAgent: timed out for tenant={TenantId}", task.TenantId);
            return Result.Fail($"GOV_AI_002: Agent request timed out.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaReActAgent: failed for tenant={TenantId}", task.TenantId);
            return Result.Fail($"GOV_AI_002: Agent unavailable — {ex.Message}");
        }
    }

    // ── Tool parsing ─────────────────────────────────────────────────────────

    private static (string tool, Dictionary<string, string> args)? TryParseToolCall(string text)
    {
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool", out var toolEl))
                return null;

            var toolName = toolEl.GetString() ?? string.Empty;
            var args = new Dictionary<string, string>();

            if (root.TryGetProperty("args", out var argsEl)
                && argsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsEl.EnumerateObject())
                    args[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
            }

            return (toolName, args);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Tool execution (READ-ONLY) ────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(
        string tool, Dictionary<string, string> args, Guid tenantId, CancellationToken ct)
    {
        return tool switch
        {
            "classify_text" => ClassifyText(args),
            "summarize" => SummarizeText(args),
            _ => $"Unknown tool '{tool}'. Available: classify_text, summarize."
        };
    }

    private static string ClassifyText(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
            return "Error: 'text' argument is required for classify_text.";

        // Simple keyword-based classification (heuristic — no ML call to avoid nested LLM)
        var lower = text.ToLowerInvariant();
        if (lower.Contains("bí mật") || lower.Contains("tuyệt mật") || lower.Contains("secret"))
            return "Classification: Secret";
        if (lower.Contains("nội bộ") || lower.Contains("confidential") || lower.Contains("mật"))
            return "Classification: Confidential";
        if (lower.Contains("công khai") || lower.Contains("public"))
            return "Classification: Public";

        return "Classification: Internal";
    }

    private static string SummarizeText(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
            return "Error: 'text' argument is required for summarize.";

        _ = int.TryParse(args.GetValueOrDefault("max_words", "100"), out var maxWords);
        maxWords = Math.Clamp(maxWords, 20, 500);

        // Simple extractive summary: take first N words
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return text;

        return string.Join(" ", words[..maxWords]) + "...";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildInitialPrompt(AgentTask task)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Task type: {task.TaskType}");
        sb.AppendLine("Parameters:");
        foreach (var (key, value) in task.Parameters)
            sb.AppendLine($"  {key}: {value}");
        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return string.Empty;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

}
