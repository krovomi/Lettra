using Lettra.Ai.Tools.Company;
using Lettra.Ai.Tools.Tools;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tools;

/// <summary>Enregistrement des outils de l'assistant et du contexte du dossier.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAiTools(this IServiceCollection services)
    {
        services.AddScoped<IAiDossierContext, AiDossierContext>();
        services.AddScoped<ICompanyFactsSource, NoCompanyFacts>();
        services.AddSingleton<IAiTool, ReadReferenceTool>();
        services.AddSingleton<IAiTool, ListReferencesTool>();
        services.AddSingleton<IAiTool, CalculateTool>();
        services.AddHttpClient<OfficialWebLookupTool>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Lettra-Assistant/1.0");
        });
        services.AddTransient<IAiTool>(sp => sp.GetRequiredService<OfficialWebLookupTool>());
        return services;
    }
}
