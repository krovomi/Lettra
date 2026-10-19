using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Demo;
using Lettra.DemoData;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Jeu de démonstration depuis l'accueil (vague 5) : un dossier vide se remplit d'un an d'activité fictive. Le chargement
/// passe par les cas d'usage, au nom de l'utilisateur et dans le dossier de la requête, et ne crée aucun autre dossier.
/// Refusé en environnement Production et sans <c>Lettra:Demo:Seed=true</c> (<see cref="LettraDemoData"/>).
/// </summary>
internal static class DemoEndpoints
{
    public static RouteGroupBuilder MapDemo(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/demo").WithTags("Demo");

        group.MapGet("/", (IConfiguration configuration, IHostEnvironment environment) =>
                TypedResults.Ok(new DemoDataStatusResponse(LettraDemoData.IsAvailable(configuration, environment.EnvironmentName))))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetDemoDataStatus");

        // Le chargement passe par les cas d'usage, qui vérifient chacun leur droit ; la porte d'entrée exige déjà le plus
        // fort d'entre eux (émettre une facture), pour qu'un compte en lecture seule ou du portail n'en lance aucun.
        group.MapPost("/seed", SeedAsync)
            .RequirePermission(Permission.FactureEmettre)
            .WithName("SeedDemoData")
            .Produces<DemoSeedResponse>();

        return group;
    }

    private static async Task<IResult> SeedAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        ITenantContext tenant,
        ICurrentUser user,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        DemoSeedResult result = await LettraDemoData.SeedAsync(
                configuration,
                environment.EnvironmentName,
                new DemoSeedTarget(tenant.Current.DossierId.Value, AdditionalDossiers: 0),
                hostServices: null,
                user.UserId,
                loggers,
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }
}
