using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Dossiers;
using Lettra.Bootstrap;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lettra.DemoData;

/// <summary>Issue d'une demande de chargement.</summary>
public enum DemoSeedStatus
{
    /// <summary>Le jeu a été chargé.</summary>
    Seeded = 1,

    /// <summary><c>Lettra:Demo:Seed</c> n'est pas activé : rien n'a été fait.</summary>
    NotRequested = 2,

    /// <summary>Environnement <c>Production</c> : refusé, quelle que soit la configuration.</summary>
    RefusedInProduction = 3,

    /// <summary>Le dossier a déjà des pièces : rien n'a été ajouté (le jeu ne se mélange pas à de vraies saisies).</summary>
    DossierNotEmpty = 4,

    /// <summary>Personne au nom de qui créer les pièces (poste : pas encore de compte ; serveur : <c>Lettra:Demo:UserId</c> absent).</summary>
    NoUser = 5,

    /// <summary>Aucun dossier à remplir.</summary>
    NoDossier = 6,
}

public sealed record DemoSeedResult(DemoSeedStatus Status, IReadOnlyList<DemoDossierReport> Dossiers, IReadOnlyList<string> Notes)
{
    public static DemoSeedResult Of(DemoSeedStatus status, string? note = null) => new(status, [], note is null ? [] : [note]);
}

/// <summary>
/// Ce qu'un chargement demandé depuis l'écran remplace dans la configuration : le dossier ouvert de l'utilisateur, et
/// en général aucun dossier créé en plus (bouton « Charger les données de démonstration » d'un dossier vide).
/// </summary>
public sealed record DemoSeedTarget(Guid? DossierId = null, int? AdditionalDossiers = null);

/// <summary>
/// Point d'entrée du jeu « Cabinet Démo » pour les hôtes (API de démonstration, application de bureau) : un appel au
/// démarrage, après <c>InitializeLettraAsync</c>. Le chargement compose sa propre instance de l'application (même
/// configuration, mêmes bases) avec une horloge rejouée et l'utilisateur de démonstration, puis la jette : l'application
/// en cours d'exécution garde son horloge, ses sessions et ses caches.
/// </summary>
public static partial class LettraDemoData
{
    /// <summary>Vrai si la configuration demande le jeu (<c>Lettra:Demo:Seed=true</c>).</summary>
    public static bool IsRequested(IConfiguration configuration) =>
        configuration?.GetSection(DemoDataOptions.SectionName).Get<DemoDataOptions>()?.Seed == true;

    /// <summary>Vrai si le jeu peut être chargé ici : demandé par la configuration et hors environnement <c>Production</c>.</summary>
    public static bool IsAvailable(IConfiguration configuration, string environmentName) =>
        IsRequested(configuration) && !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Charge le jeu si la configuration le demande, hors <c>Production</c> et seulement si le dossier n'a encore aucune pièce.
    /// </summary>
    /// <param name="configuration">La configuration de l'hôte (mode de déploiement, bases, section <c>Lettra:Demo</c>).</param>
    /// <param name="environmentName">Environnement de l'hôte ; <c>Production</c> refuse toujours.</param>
    /// <param name="hostServices">Ce que l'hôte ajoute à <c>AddLettra</c> (poste : le coffre de clés de l'OS).</param>
    /// <param name="userId">Utilisateur au nom de qui charger (poste : le compte ouvert), sinon <c>Lettra:Demo:UserId</c>.</param>
    /// <param name="loggerFactory">Journal de l'hôte.</param>
    /// <param name="cancellationToken">Annulation.</param>
    public static Task<DemoSeedResult> SeedAsync(
        IConfiguration configuration,
        string environmentName,
        Action<IServiceCollection>? hostServices = null,
        Guid? userId = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default) =>
        SeedAsync(configuration, environmentName, target: null, hostServices, userId, loggerFactory, cancellationToken);

    /// <summary>Depuis l'écran : comme au démarrage, mais pour le dossier et le nombre de dossiers en plus donnés par <paramref name="target"/>.</summary>
    /// <param name="configuration">La configuration de l'hôte.</param>
    /// <param name="environmentName">Environnement de l'hôte ; <c>Production</c> refuse toujours.</param>
    /// <param name="target">Le dossier à remplir et le nombre de dossiers créés en plus, à la place de la configuration ; null : la configuration.</param>
    /// <param name="hostServices">Ce que l'hôte ajoute à <c>AddLettra</c>.</param>
    /// <param name="userId">Utilisateur au nom de qui charger.</param>
    /// <param name="loggerFactory">Journal de l'hôte.</param>
    /// <param name="cancellationToken">Annulation.</param>
    public static async Task<DemoSeedResult> SeedAsync(
        IConfiguration configuration,
        string environmentName,
        DemoSeedTarget? target,
        Action<IServiceCollection>? hostServices,
        Guid? userId,
        ILoggerFactory? loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DemoDataOptions options = configuration.GetSection(DemoDataOptions.SectionName).Get<DemoDataOptions>() ?? new DemoDataOptions();
        options.DossierId = target?.DossierId ?? options.DossierId;
        options.AdditionalDossiers = target?.AdditionalDossiers ?? options.AdditionalDossiers;
        ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(typeof(LettraDemoData));
        if (!options.Seed)
        {
            return DemoSeedResult.Of(DemoSeedStatus.NotRequested);
        }

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            LogRefusedInProduction(logger);
            return DemoSeedResult.Of(DemoSeedStatus.RefusedInProduction);
        }

        Guid user = userId ?? options.UserId ?? Guid.Empty;
        if (user == Guid.Empty)
        {
            return DemoSeedResult.Of(DemoSeedStatus.NoUser);
        }

        DemoClock clock = new();
        clock.Set(TimeProvider.System.GetUtcNow());
        DemoSession session = new(user);
        await using ServiceProvider services = Compose(configuration, hostServices, clock, session, loggerFactory);

        Guid? chosen = options.DossierId ?? await DefaultDossierAsync(services, cancellationToken).ConfigureAwait(false);
        if (chosen is not Guid dossier)
        {
            return DemoSeedResult.Of(DemoSeedStatus.NoDossier);
        }

        session.CurrentDossierId = dossier;
        bool alreadySeeded = await new DemoDossierLoader(services, clock).HasDocumentsAsync(cancellationToken).ConfigureAwait(false);
        if (alreadySeeded)
        {
            LogNotEmpty(logger, dossier);
        }

        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")).DateTime);
        List<DemoDossierReport> reports = [];
        List<string> notes = [];
        LocalDossierRegistry? registry = services.GetService<LocalDossierRegistry>();
        Guid? active = registry?.ActiveDossierId;

        if (!alreadySeeded)
        {
            reports.Add(await LoadAsync(services, clock, dossier, DemoBusiness.Services, options, today, cancellationToken).ConfigureAwait(false));
        }

        // Les autres dossiers du cabinet (vue cabinet) : créés une seule fois, y compris dans une démonstration déjà
        // remplie par une version précédente. Un dossier qui porte déjà le nom de l'entreprise n'est pas recréé.
        OrganisationId? cabinet = (await services.GetRequiredService<ITenantCatalog>().FindAsync(new DossierId(dossier), cancellationToken).ConfigureAwait(false))?.OrganisationId;
        IReadOnlyList<DossierSummary> existing = cabinet is OrganisationId known
            ? await services.GetRequiredService<IDossierDirectory>().ListAsync(known, cancellationToken).ConfigureAwait(false)
            : [];
        foreach (DemoBusiness business in new[] { DemoBusiness.Building, DemoBusiness.Trading }.Take(Math.Clamp(options.AdditionalDossiers, 0, 2)))
        {
            if (cabinet is not OrganisationId organisation)
            {
                break;
            }

            string name = DemoCompanies.For(business).Name;
            if (existing.Any(d => string.Equals(d.Name, name, StringComparison.Ordinal)))
            {
                continue;
            }

            clock.Set(TimeProvider.System.GetUtcNow());
            Result<DossierSummary> created = await CreateDossierAsync(services, organisation, name, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                // Le serveur de démonstration peut ne pas donner le droit de créer des dossiers : le dossier ouvert suffit.
                notes.Add($"Dossier « {name} » non créé : {created.Errors[0].Code}.");
                LogDossierNotCreated(logger, business, created.Errors[0].Code);
                break;
            }

            session.CurrentDossierId = created.Value.Id;
            reports.Add(await LoadAsync(services, clock, created.Value.Id, business, options, today, cancellationToken).ConfigureAwait(false));
        }

        // Créer un dossier sur le poste l'ouvre : on rouvre celui de l'utilisateur.
        if (registry is not null && active is Guid opened && registry.ActiveDossierId != opened)
        {
            await registry.SetActiveAsync(new DossierId(opened), cancellationToken).ConfigureAwait(false);
        }

        session.CurrentDossierId = dossier;
        if (alreadySeeded)
        {
            return new DemoSeedResult(DemoSeedStatus.DossierNotEmpty, reports, notes);
        }

        if (cabinet is OrganisationId owner)
        {
            await RaiseAlertsAsync(services, owner, dossier, today, cancellationToken).ConfigureAwait(false);
        }

        foreach (DemoDossierReport report in reports)
        {
            LogSeeded(logger, report.Business, report.DossierId, report.Quotes, report.Invoices, report.Duration.TotalSeconds);
        }

        return new DemoSeedResult(DemoSeedStatus.Seeded, reports, notes);
    }

    private static ServiceProvider Compose(
        IConfiguration configuration, Action<IServiceCollection>? hostServices, DemoClock clock, DemoSession session, ILoggerFactory? loggerFactory)
    {
        ServiceCollection services = new();
        if (loggerFactory is null)
        {
            services.AddLogging();
        }
        else
        {
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        }

        services.AddSingleton(configuration);
        services.AddLettra(configuration);
        hostServices?.Invoke(services);
        services.AddInProcessGateways();

        // Seules différences avec l'application : l'horloge rejouée, l'utilisateur de démonstration et le dossier rempli.
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        services.RemoveAll<ICurrentUser>();
        services.AddSingleton<ICurrentUser>(session);
        services.RemoveAll<IDossierSelection>();
        services.AddSingleton<IDossierSelection>(session);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<DemoDossierReport> LoadAsync(
        ServiceProvider services, DemoClock clock, Guid dossier, DemoBusiness business, DemoDataOptions options, DateOnly today, CancellationToken cancellationToken)
    {
        DemoScenario scenario = DemoScenarioGenerator.Generate(business, options.RandomSeed, today, Math.Clamp(options.Months, 2, 36));
        return await new DemoDossierLoader(services, clock).LoadAsync(dossier, scenario, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Poste : le dossier ouvert ; serveur : le premier dossier que l'utilisateur de démonstration peut ouvrir.</summary>
    private static async Task<Guid?> DefaultDossierAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<LocalDossierRegistry>()?.ActiveDossierId is Guid active)
        {
            return active;
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        Result<IReadOnlyList<DossierSummary>> accessible = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
            .QueryAsync(new ListAccessibleDossiersQuery(), cancellationToken).ConfigureAwait(false);
        return accessible.IsSuccess && accessible.Value.Count > 0 ? accessible.Value[0].Id : null;
    }

    /// <summary>
    /// Par le cas d'usage, avec les droits de l'utilisateur de démonstration, sur le poste comme sur le serveur : sur le
    /// poste, son propriétaire administre le cabinet du registre local (<c>ILocalCabinet</c>).
    /// </summary>
    private static async Task<Result<DossierSummary>> CreateDossierAsync(
        ServiceProvider services, OrganisationId organisation, string name, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new CreateDossierCommand(organisation, name), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Quelques alertes dans le centre d'alertes, par l'entrée normale des signaux (comme l'Alertmanager de la cellule) :
    /// deux ouvertes, une résolue. Titres de règles seulement, aucune donnée de dossier. Le poste ne montre que les
    /// alertes du dossier ouvert ; celle du cabinet se voit sur le serveur.
    /// </summary>
    private static async Task RaiseAlertsAsync(ServiceProvider services, OrganisationId cabinet, Guid dossier, DateOnly today, CancellationToken cancellationToken)
    {
        DateTimeOffset At(int daysAgo, int hour) => new(today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
        Result<AlertScope> onCabinet = AlertScope.ForCabinet(null, cabinet.Value);
        Result<AlertScope> onDossier = AlertScope.ForDossier(null, cabinet.Value, dossier);
        if (onCabinet.IsFailure || onDossier.IsFailure)
        {
            return;
        }

        AlertSignal[] signals =
        [
            new("SauvegardeManquee", "Sauvegarde nocturne manquée", Severity.S2, AlertCategory.Availability, onDossier.Value, Firing: true, At(21, 3)),
            new("SauvegardeManquee", "Sauvegarde nocturne manquée", Severity.S2, AlertCategory.Availability, onDossier.Value, Firing: false, At(20, 4)),
            new("LettraValidationsLentes", "Validations de factures plus lentes que l'objectif", Severity.S3, AlertCategory.Performance, onDossier.Value, Firing: true, At(2, 10)),
            new("DisqueBasesSeRemplit", "Le disque des bases se remplit", Severity.S3, AlertCategory.Availability, onCabinet.Value, Firing: true, At(1, 16)),
        ];
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<AlertIngestion>() is not AlertIngestion ingestion)
        {
            return;
        }

        foreach (AlertSignal signal in signals)
        {
            _ = await ingestion.IngestAsync(signal, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jeu de démonstration refusé : l'environnement est Production.")]
    private static partial void LogRefusedInProduction(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jeu de démonstration non chargé : le dossier {Dossier} a déjà des pièces.")]
    private static partial void LogNotEmpty(ILogger logger, Guid dossier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jeu de démonstration : dossier {Business} non créé ({Code}).")]
    private static partial void LogDossierNotCreated(ILogger logger, DemoBusiness business, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jeu de démonstration {Business} chargé dans {Dossier} : {Quotes} devis, {Invoices} factures en {Seconds:0.0} s.")]
    private static partial void LogSeeded(ILogger logger, DemoBusiness business, Guid dossier, int quotes, int invoices, double seconds);
}
