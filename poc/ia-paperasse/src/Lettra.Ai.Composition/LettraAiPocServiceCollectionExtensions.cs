using Anthropic;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Claude;
using Lettra.Ai.Local;
using Lettra.Ai.Orchestration;
using Lettra.Ai.Skills;
using Lettra.Ai.Stub;
using Lettra.Ai.Tools;
using Lettra.Ai.Tools.Tools;
using Lettra.Poc.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Composition;

public sealed record LettraAiPocOptions
{
    /// <summary>Active le moteur Claude. Par défaut : seulement si une clé ANTHROPIC_API_KEY est présente.</summary>
    public bool EnableClaude { get; init; } = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
    public ClaudeEngineOptions Claude { get; init; } = new();
    /// <summary>Point d'accès du modèle local (LETTRA_LOCAL_MODEL_URL) ; absent = moteur local indisponible.</summary>
    public Uri? LocalEndpoint { get; init; } = Uri.TryCreate(Environment.GetEnvironmentVariable("LETTRA_LOCAL_MODEL_URL"), UriKind.Absolute, out var u) ? u : null;
    public string LocalModel { get; init; } = Environment.GetEnvironmentVariable("LETTRA_LOCAL_MODEL") ?? "mistral-small";
}

/// <summary>Composition root du POC : mêmes briques que dans Lettra.Api, avec un dossier fictif en mémoire.</summary>
public static class LettraAiPocServiceCollectionExtensions
{
    public static IServiceCollection AddLettraAiPoc(this IServiceCollection services, LettraAiPocOptions? options = null)
    {
        options ??= new LettraAiPocOptions();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock>(sp => new TimeProviderClock(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(_ => SampleDossiers.CreateStore());
        services.AddSingleton<DossierUseCases>();

        services.AddSingleton<IAiSkillCatalog>(_ => SkillCatalog.LoadEmbedded());
        services.AddSingleton<IDossierContextProvider, DossierContextProvider>();
        services.AddSingleton<IAiAuditLog, InMemoryAiAuditLog>();
        services.AddSingleton<InMemoryAiUsagePolicy>();
        services.AddSingleton<IAiUsagePolicy>(sp => sp.GetRequiredService<InMemoryAiUsagePolicy>());

        services.AddHttpClient(nameof(OfficialWebLookupTool), c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IAiTool, ReadReferenceTool>();
        services.AddSingleton<IAiTool, ListReferencesTool>();
        services.AddSingleton<IAiTool, DossierContextTool>();
        services.AddSingleton<IAiTool, ValidateInvoiceTool>();
        services.AddSingleton<IAiTool, SearchClientTool>();
        services.AddSingleton<IAiTool, DraftQuoteTool>();
        services.AddSingleton<IAiTool, CalculateTool>();
        services.AddSingleton<IAiTool>(sp => new OfficialWebLookupTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OfficialWebLookupTool))));
        services.AddSingleton(sp => new ToolRegistry(sp.GetServices<IAiTool>()));

        services.AddHttpClient(nameof(LocalModelEngine));
        services.AddSingleton<IAiEngineFactory>(sp =>
        {
            var engines = new Dictionary<AiEngineKind, IAiEngine> { [AiEngineKind.Stub] = new ScriptedAccountantEngine() };
            if (options.EnableClaude)
                engines[AiEngineKind.ClaudeEu] = new ClaudeEngine(new AnthropicClient(), options.Claude);
            if (options.LocalEndpoint is { } endpoint)
                engines[AiEngineKind.Local] = new LocalModelEngine(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LocalModelEngine)),
                    new LocalModelOptions { Endpoint = endpoint, Model = options.LocalModel });
            return new AiEngineFactory(engines);
        });

        services.AddSingleton<IAccountingAssistant, AccountingAssistant>();
        return services;
    }

    private sealed class TimeProviderClock(TimeProvider time) : IClock
    {
        public DateTimeOffset Now => time.GetUtcNow();
    }
}
