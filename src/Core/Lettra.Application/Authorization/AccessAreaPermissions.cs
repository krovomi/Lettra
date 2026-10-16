using System.Collections.Frozen;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Administration;

namespace Lettra.Application.Authorization;

/// <summary>
/// Traduit les espaces de Lettra (<see cref="AccessAreas"/>) en permissions, et l'inverse. Chaque niveau d'un espace
/// ajoute des permissions à celles du niveau précédent : « Travailler » sur les factures donne aussi « Voir ».
/// <para>
/// Lecture : un ensemble de permissions a le niveau le plus haut dont toutes les permissions <b>propres</b> sont
/// présentes. Une délégation (avoirs, sans lecture du dossier) se lit donc « Travailler » sur les avoirs, comme elle se
/// cumule en pratique avec le rôle de la personne.
/// </para>
/// </summary>
public static class AccessAreaPermissions
{
    private static readonly FrozenDictionary<AccessAreaDto, (AccessLevelDto Level, Permission[] Adds)[]> Levels =
        new Dictionary<AccessAreaDto, (AccessLevelDto, Permission[])[]>
        {
            [AccessAreaDto.Quotes] =
            [
                (AccessLevelDto.View, [Permission.DossierLire]),
                (AccessLevelDto.Work, [Permission.DevisCreer]),
            ],
            [AccessAreaDto.Invoices] =
            [
                (AccessLevelDto.View, [Permission.DossierLire]),
                (AccessLevelDto.Work, [Permission.FactureSaisir]),
                (AccessLevelDto.Approve, [Permission.FactureValider, Permission.FactureEmettre]),
            ],
            [AccessAreaDto.CreditNotes] =
            [
                (AccessLevelDto.View, [Permission.DossierLire]),
                (AccessLevelDto.Work, [Permission.AvoirCreer]),
                (AccessLevelDto.Approve, [Permission.AvoirApprouver]),
            ],
            [AccessAreaDto.Parties] =
            [
                (AccessLevelDto.View, [Permission.TiersConsulter]),
                (AccessLevelDto.Work, [Permission.TiersCreer, Permission.TiersModifier]),
            ],
            [AccessAreaDto.Catalog] =
            [
                (AccessLevelDto.View, [Permission.CatalogueConsulter]),
                (AccessLevelDto.Work, [Permission.CatalogueGerer]),
            ],
            [AccessAreaDto.Accounting] =
            [
                (AccessLevelDto.View, [Permission.IntegriteVerifier]),
                (AccessLevelDto.Work, [Permission.FecExporter]),
                (AccessLevelDto.Approve, [Permission.ClotureEffectuer, Permission.ArchivesExporter, Permission.IntegriteClesGerer]),
            ],
            [AccessAreaDto.Alerts] =
            [
                (AccessLevelDto.View, [Permission.AlertesLire]),
                (AccessLevelDto.Work, [Permission.AlertesTraiter]),
                (AccessLevelDto.Approve, [Permission.AlertesConfigurer]),
            ],
            [AccessAreaDto.Assistant] = [(AccessLevelDto.Work, [Permission.IaUtiliser])],
            [AccessAreaDto.Devices] = [(AccessLevelDto.Work, [Permission.PostesGerer])],
            [AccessAreaDto.Administration] =
            [
                (AccessLevelDto.View, [Permission.DossierAdministrer]),
                (AccessLevelDto.Work, [Permission.GroupeGerer]),
                (AccessLevelDto.Approve, [Permission.UtilisateursGerer]),
            ],
            [AccessAreaDto.Portal] =
            [
                (AccessLevelDto.View, [Permission.PortailConsulter]),
                (AccessLevelDto.Work, [Permission.DevisAccepter, Permission.PiecesDeposer]),
            ],
        }.ToFrozenDictionary();

    /// <summary>Les espaces ouverts par ces permissions, dans l'ordre de <see cref="AccessAreas.All"/>, sans les espaces fermés.</summary>
    public static IReadOnlyList<AreaAccess> Describe(IReadOnlySet<Permission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        List<AreaAccess> areas = [];
        foreach (AccessAreaInfo info in AccessAreas.All)
        {
            AccessLevelDto level = AccessLevelDto.None;
            foreach ((AccessLevelDto candidate, Permission[] adds) in Levels[info.Area])
            {
                if (adds.All(permissions.Contains))
                {
                    level = candidate;
                }
            }

            if (level != AccessLevelDto.None)
            {
                areas.Add(new AreaAccess(info.Area, level));
            }
        }

        return areas;
    }

    /// <summary>
    /// Les permissions d'un rôle sur mesure. Seuls les espaces <see cref="AccessAreaInfo.Customizable"/> et leurs
    /// niveaux proposés sont acceptés : l'administration et le portail restent dans les rôles fournis.
    /// </summary>
    public static IReadOnlySet<Permission>? Compose(IEnumerable<AreaAccess> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        HashSet<Permission> permissions = [];
        foreach (AreaAccess access in areas)
        {
            if (access.Level == AccessLevelDto.None)
            {
                continue;
            }

            AccessAreaInfo? info = AccessAreas.All.FirstOrDefault(a => a.Area == access.Area);
            if (info is not { Customizable: true } || !info.Levels.Contains(access.Level))
            {
                return null;
            }

            foreach ((AccessLevelDto level, Permission[] adds) in Levels[access.Area])
            {
                if (level <= access.Level)
                {
                    permissions.UnionWith(adds);
                }
            }
        }

        return permissions;
    }
}
