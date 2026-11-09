using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.Ai.Infrastructure.Repositories;
using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.Ai.Infrastructure.VectorStore;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace AqtCoreFW.Ai.Infrastructure;

/// <summary>
/// DI registration entry point for the AI Infrastructure layer.
/// Conditional on AI:Ollama:Enabled — stubs used when Ollama is not configured.
/// Call after application services are registered (MediatR etc. already registered).
/// </summary>
public static class AiInfrastructureRegistration
{
    public static IServiceCollection AddAiInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<AiDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "ai"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IAiSessionRepository, AiSessionRepository>();
        services.AddScoped<IAiModelProfileRepository, AiModelProfileRepository>();
        services.AddScoped<IAiPromptTemplateRepository, AiPromptTemplateRepository>();

        // ── Options ───────────────────────────────────────────────────────────
        services.Configure<OllamaOptions>(configuration.GetSection("AI:Ollama"));
        services.Configure<QdrantOptions>(configuration.GetSection("AI:Qdrant"));

        // ── Always-stub services (project-specific — replace by registering before this call) ──
        services.TryAddSingleton<IAiDecisionSupport, AiDecisionSupportStub>();
        services.TryAddSingleton<IAiRiskScorer, AiRiskScorerStub>();
        services.TryAddSingleton<IAiReportAnalyst, AiReportAnalystStub>();
        // IAiAgent and IAiDocumentExtractor registered conditionally below (Ollama path uses real impls)

        // ── Data sovereignty checker (always real — pure function, no deps) ──
        services.TryAddSingleton<IDataSovereigntyChecker, DataSovereigntyChecker>();

        // ── Citizen chatbot (always registered — uses IAiRoutingService) ──
        services.TryAddScoped<ICitizenChatbotService, CitizenChatbotService>();

        // ── Model profile provider (always real — reads from AiDbContext) ──
        services.TryAddScoped<IAiModelProfileProvider, InProcessAiModelProfileProvider>();

        // ── Token budget tracker (Redis-backed, fails open if Redis unavailable) ──
        services.TryAddSingleton<ITokenBudgetTracker, TokenBudgetTracker>();

        // ── Azure OpenAI services — gracefully disabled if config not present ──
        services.TryAddSingleton<AzureOpenAiChatService>();
        services.TryAddSingleton<AzureOpenAiStreamingChatService>();

        // ── Conditional Ollama/Qdrant services ───────────────────────────────
        var ollamaEnabled = configuration.GetSection("AI:Ollama")
            .GetValue<bool>("Enabled", defaultValue: false);

        if (ollamaEnabled)
        {
            RegisterOllamaServices(services, configuration);
            // Real implementations backed by IChatClient + IReadDbConnection
            services.AddScoped<IAiDocumentExtractor, OllamaDocumentExtractor>();
            services.AddScoped<IAiAgent, OllamaReActAgent>();
            services.AddScoped<IAiAutoFlaggingService, OllamaAutoFlaggingService>();
        }
        else
        {
            RegisterStubServices(services);
            // Stubs for services not registered in RegisterStubServices
            services.TryAddScoped<IAiDocumentExtractor, AiDocumentExtractorStub>();
            services.TryAddScoped<IAiAgent, AiAgentStub>();
            services.TryAddScoped<IAiAutoFlaggingService, AiAutoFlaggingServiceStub>();
        }

        return services;
    }

    private static void RegisterOllamaServices(
        IServiceCollection services, IConfiguration configuration)
    {
        var ollamaEndpoint = configuration["AI:Ollama:Endpoint"] ?? "http://localhost:11434";
        var chatModel = configuration["AI:Ollama:ChatModel"] ?? "qwen2.5:7b";
        var embedModel = configuration["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        // Microsoft.Extensions.AI Ollama clients
        services.AddSingleton<IChatClient>(_ =>
            new OllamaChatClient(new Uri(ollamaEndpoint), chatModel));

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            new OllamaEmbeddingGenerator(new Uri(ollamaEndpoint), embedModel));

        // Inference queue — singleton service + background consumer
        services.AddSingleton<OllamaInferenceQueueService>();
        services.AddHostedService<OllamaInferenceQueue>();

        // Chat + embedding services
        services.AddSingleton<ILocalAiChatService, OllamaChatService>();
        services.AddSingleton<ILocalAiStreamingChatService, OllamaStreamingChatService>();

        // OllamaEmbeddingService registered as concrete type; CachedEmbeddingService wraps it.
        // Manual decorator pattern (no Scrutor dependency).
        services.AddSingleton<OllamaEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp =>
            new CachedEmbeddingService(
                inner: sp.GetRequiredService<OllamaEmbeddingService>(),
                cache: sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
                logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedEmbeddingService>>()));

        // Qdrant — IHttpClientFactory pattern avoids scoped/singleton lifetime mismatch
        services.AddHttpClient("qdrant", (sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value;
            http.BaseAddress = new Uri(opts.Endpoint);
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                http.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
        });
        services.AddSingleton<IQdrantVectorStore>(sp =>
        {
            var factory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var http = factory.CreateClient("qdrant");
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QdrantVectorStore>>();
            return new QdrantVectorStore(http, opts, logger);
        });
        services.AddSingleton<IVectorStore, QdrantVectorStoreAdapter>();

        // Application-layer AI services (scoped — use DbContext)
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IRagService, RagService>();

        // AiModelRouter: profile-aware routing (replaces plain AiRoutingService when Ollama enabled)
        services.AddScoped<IAiRoutingService, AiModelRouter>();

        // Cloud AI — stub by default; projects override before calling AddAiInfrastructure
        services.TryAddSingleton<ICloudAiChatService, CloudAiChatServiceStub>();
        services.TryAddSingleton<ICloudAiStreamingChatService, CloudAiStreamingChatServiceStub>();
    }

    private static void RegisterStubServices(IServiceCollection services)
    {
        // All Ollama/Qdrant-backed services → safe no-op stubs
        services.TryAddSingleton<ILocalAiChatService, LocalAiChatServiceStub>();
        services.TryAddSingleton<ILocalAiStreamingChatService, LocalAiStreamingChatServiceStub>();
        services.TryAddSingleton<IEmbeddingService, EmbeddingServiceStub>();
        services.TryAddSingleton<ICloudAiChatService, CloudAiChatServiceStub>();
        services.TryAddSingleton<ICloudAiStreamingChatService, CloudAiStreamingChatServiceStub>();
        services.TryAddSingleton<IDocumentIngestionService, DocumentIngestionServiceStub>();
        services.TryAddSingleton<IRagService, RagServiceStub>();
        services.TryAddSingleton<IAiRoutingService, AiRoutingServiceStub>();
    }
}
