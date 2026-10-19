using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Pilotage;
using Lettra.Domain.Common;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Tableaux de bord dans Lettra (docs/tableaux-de-bord.md) : les indicateurs des tableaux Grafana, avec les droits de
/// Lettra. Le dossier ouvert est calculé à l'instant ; « Mes dossiers » et « Administration » lisent la dernière collecte,
/// filtrée dossier par dossier.
/// </summary>
internal static class PilotageEndpoints
{
    /// <summary>Sous <c>/api/v1/dossiers</c>, hors de tout dossier.</summary>
    public static RouteGroupBuilder MapCabinetPilotage(this RouteGroupBuilder dossiers)
    {
        dossiers.MapGet("/pilotage", GetCabinetAsync)
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Pilotage")
            .WithName("GetCabinetPilotage")
            .Produces<CabinetPilotageResponse>();

        dossiers.MapGet("/pilotage/administration", GetAdministrationAsync)
            .FilterByPermission(Permission.DossierAdministrer)
            .WithTags("Pilotage")
            .WithName("GetAdministrationPilotage")
            .Produces<AdministrationPilotageResponse>();
        return dossiers;
    }

    /// <summary>Dans le dossier ouvert (en-tête <c>X-Dossier</c>).</summary>
    public static RouteGroupBuilder MapDossierPilotage(this RouteGroupBuilder api)
    {
        api.MapGet("/pilotage", GetDossierAsync)
            .RequirePermission(Permission.DossierLire)
            .WithTags("Pilotage")
            .WithName("GetDossierPilotage")
            .Produces<DossierPilotageResponse>();
        return api;
    }

    private static async Task<IResult> GetDossierAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<DossierPilotageResponse> pilotage = await queries
            .QueryAsync(new GetDossierPilotageQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return Ok(pilotage);
    }

    private static async Task<IResult> GetCabinetAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<CabinetPilotageResponse> pilotage = await queries.QueryAsync(new GetCabinetPilotageQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(pilotage);
    }

    private static async Task<IResult> GetAdministrationAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AdministrationPilotageResponse> pilotage = await queries
            .QueryAsync(new GetAdministrationPilotageQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(pilotage);
    }
}
