using AqtCoreFW.Ai.Domain.Errors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// CPU-only Ollama inference serialization queue — prevents concurrent LLM calls on CPU.
/// Single consumer loop: dequeues and executes one job at a time.
/// Bounded channel capacity = 50; full → throws GOV_AI_004.
/// Job wait timeout: 60 seconds.
/// </summary>
public sealed class OllamaInferenceQueueService
{
    private const int Capacity = 50;
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(60);

    private readonly Channel<OllamaInferenceJob> _channel =
        Channel.CreateBounded<OllamaInferenceJob>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,  // signals queue full to caller
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ILogger<OllamaInferenceQueueService> _logger;

    public OllamaInferenceQueueService(ILogger<OllamaInferenceQueueService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enqueue inference work and await result. Throws if queue is full or job times out.
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<object?> Wrapper(CancellationToken innerCt)
        {
            var result = await work(innerCt);
            return result;
        }

        var job = new OllamaInferenceJob(Wrapper, tcs);

        var written = _channel.Writer.TryWrite(job);
        if (!written)
        {
            _logger.LogWarning("GOV_AI_004: Ollama inference queue full (capacity={Capacity})", Capacity);
            throw new OllamaQueueFullException(AiErrors.InferenceQueueFull().Message);
        }

        // Wait for completion with timeout
        using var timeoutCts = new CancellationTokenSource(JobTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));

        var result = await tcs.Task;
        return (T)result!;
    }

    /// <summary>Consumer loop — called by the hosted BackgroundService.</summary>
    internal async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = await job.Work(stoppingToken);
                job.Tcs.TrySetResult(result);
            }
            catch (OperationCanceledException ex)
            {
                job.Tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OllamaInferenceQueue: job execution failed");
                job.Tcs.TrySetException(ex);
            }
        }
    }

    // Internal job record
    internal sealed record OllamaInferenceJob(
        Func<CancellationToken, Task<object?>> Work,
        TaskCompletionSource<object?> Tcs);
}

/// <summary>
/// BackgroundService wrapper — drives the single consumer loop of OllamaInferenceQueueService.
/// Registered as hosted service; OllamaInferenceQueueService is the injectable singleton.
/// </summary>
public sealed class OllamaInferenceQueue(
    OllamaInferenceQueueService queueService,
    ILogger<OllamaInferenceQueue> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OllamaInferenceQueue consumer started.");
        await queueService.ConsumeAsync(stoppingToken);
        logger.LogInformation("OllamaInferenceQueue consumer stopped.");
    }
}

/// <summary>Thrown when the bounded inference channel is at capacity (GOV_AI_004).</summary>
public sealed class OllamaQueueFullException(string message) : InvalidOperationException(message);
