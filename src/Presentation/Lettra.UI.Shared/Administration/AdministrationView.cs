using System.Globalization;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Rights;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Administration;

/// <summary>
/// Ce que l'espace « Équipe et accès » calcule pour l'affichage, à partir de la lecture du registre : libellés en langage
/// simple, accès cumulés d'une personne, compteurs. Fonctions pures : testées sans rendu.
/// </summary>
public sealed class AdministrationView(AdministrationOverview overview, IStringLocalizer localizer)
{
    private readonly Dictionary<Guid, PersonSummary> _people = overview.People.ToDictionary(p => p.Id);
    private readonly Dictionary<Guid, TeamSummary> _teams = overview.Teams.ToDictionary(t => t.Id);
    private readonly Dictionary<Guid, string> _dossiers = overview.Dossiers.ToDictionary(d => d.Id, d => d.Name);
    private readonly Dictionary<string, RoleDescription> _roles = overview.Roles.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public AdministrationOverview Overview { get; } = overview;

    public PersonSummary? Person(Guid id) => _people.GetValueOrDefault(id);

    public TeamSummary? Team(Guid id) => _teams.GetValueOrDefault(id);

    public RoleDescription? Role(string key) => _roles.GetValueOrDefault(key);

    public int ActiveCount => Overview.People.Count(p => p.State == AccountStateDto.Active);

    public int PendingCount => Overview.People.Count(p => p.State == AccountStateDto.Invited);

    /// <summary>Attributions à durée limitée (auditeur, délégation, remplacement) encore en cours.</summary>
    public int TemporaryCount(DateTimeOffset now) =>
        Overview.People.SelectMany(p => p.Assignments).Concat(Overview.Teams.SelectMany(t => t.Assignments)).Count(a => a.ExpiresAt > now);

    /// <summary>Nom affiché : le nom, sinon l'adresse, sinon « Compte sans nom ».</summary>
    public string NameOf(PersonSummary person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return person.DisplayName ?? person.Email ?? localizer["Admin.Person.Unnamed"];
    }

    /// <summary>Le prénom (premier mot du nom) pour les phrases : « Ce que Camille peut faire ».</summary>
    public string FirstNameOf(PersonSummary person)
    {
        string name = NameOf(person);
        int space = name.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? name[..space] : name;
    }

    public static string Initials(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string[] words = name.Split([' ', '-', '.', '@'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0])),
        };
    }

    /// <summary>Teinte stable par personne (1 à 6), pour reconnaître un avatar d'un coup d'œil.</summary>
    public static int Hue(Guid id) => (int)(BitConverter.ToUInt32(id.ToByteArray(), 12) % 6) + 1;

    public string RoleName(string key)
    {
        RoleDescription? role = Role(key);
        return role is { IsCustom: true, Name: { } name } ? name : RoleName(localizer, key);
    }

    public string RoleSummary(string key)
    {
        RoleDescription? role = Role(key);
        return role is { IsCustom: true } ? role.Description ?? localizer["Admin.Role.CustomHint"] : localizer[$"Admin.Role.{key}.Desc"];
    }

    public static string RoleName(IStringLocalizer localizer, string key)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        LocalizedString name = localizer[$"Admin.Role.{key}.Name"];
        return name.ResourceNotFound ? key : name.Value;
    }

    /// <summary>« Tout le cabinet », « Équipe Paris », « Dossier Boulangerie Martin ».</summary>
    public string ScopeLabel(ScopeKindDto kind, Guid id) => kind switch
    {
        ScopeKindDto.Cabinet => localizer["Admin.Scope.Cabinet"],
        ScopeKindDto.Groupe => string.Format(CultureInfo.CurrentCulture, localizer["Admin.Scope.TeamNamed"], Team(id)?.Name ?? localizer["Admin.Scope.Unknown"]),
        _ => string.Format(CultureInfo.CurrentCulture, localizer["Admin.Scope.DossierNamed"], _dossiers.GetValueOrDefault(id) ?? localizer["Admin.Scope.Unknown"]),
    };

    public string DossierName(Guid id) => _dossiers.GetValueOrDefault(id) ?? localizer["Admin.Scope.Unknown"];

    /// <summary>Les attributions qui viennent des équipes de la personne (affichées « par l'équipe … »).</summary>
    public IEnumerable<(TeamSummary Team, RoleAssignmentSummary Assignment)> InheritedAssignments(PersonSummary person)
    {
        ArgumentNullException.ThrowIfNull(person);
        foreach (Guid teamId in person.TeamIds)
        {
            if (Team(teamId) is { } team)
            {
                foreach (RoleAssignmentSummary assignment in team.Assignments)
                {
                    yield return (team, assignment);
                }
            }
        }
    }

    /// <summary>
    /// Ce que la personne peut faire, toutes portées confondues : pour chaque espace, le niveau le plus haut que lui donne
    /// l'un de ses rôles (directs ou par ses équipes), à une date donnée. Une personne suspendue ou invitée n'a rien.
    /// </summary>
    public IReadOnlyList<AreaAccess> EffectiveAreas(PersonSummary person, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(person);
        if (person.State != AccountStateDto.Active)
        {
            return [];
        }

        return Merge(person.Assignments.Concat(InheritedAssignments(person).Select(i => i.Assignment))
            .Where(a => a.ExpiresAt is not DateTimeOffset end || end > now)
            .Select(a => Role(a.Role))
            .OfType<RoleDescription>()
            .SelectMany(r => r.Areas));
    }

    /// <summary>Niveau le plus haut par espace, dans l'ordre des espaces.</summary>
    public static IReadOnlyList<AreaAccess> Merge(IEnumerable<AreaAccess> areas)
    {
        Dictionary<AccessAreaDto, AccessLevelDto> best = [];
        foreach (AreaAccess access in areas)
        {
            if (access.Level > best.GetValueOrDefault(access.Area))
            {
                best[access.Area] = access.Level;
            }
        }

        return AccessAreas.All.Where(a => best.ContainsKey(a.Area)).Select(a => new AreaAccess(a.Area, best[a.Area])).ToArray();
    }

    /// <summary>
    /// Niveaux d'un rôle sur mesure tels qu'ils seront enregistrés : devis, factures et avoirs partagent « Voir ».
    /// </summary>
    public static IReadOnlyList<AreaAccess> Normalize(IReadOnlyDictionary<AccessAreaDto, AccessLevelDto> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        bool salesView = AccessAreas.All.Any(a => a.SharesSalesView && chosen.GetValueOrDefault(a.Area) >= AccessLevelDto.View);
        return AccessAreas.All
            .Where(a => a.Customizable)
            .Select(a => new AreaAccess(a.Area, a.SharesSalesView && salesView && chosen.GetValueOrDefault(a.Area) == AccessLevelDto.None
                ? AccessLevelDto.View
                : chosen.GetValueOrDefault(a.Area)))
            .Where(a => a.Level != AccessLevelDto.None)
            .ToArray();
    }

    /// <summary>Le rôle peut-il être donné à cette portée (un rôle d'équipe ne se donne pas sur tout le cabinet) ?</summary>
    public static bool Allows(RoleDescription role, ScopeKindDto scope)
    {
        ArgumentNullException.ThrowIfNull(role);
        return scope >= role.WidestScope;
    }

    public IEnumerable<PersonSummary> Search(string? text, PeopleFilter filter)
    {
        IEnumerable<PersonSummary> people = filter switch
        {
            PeopleFilter.Active => Overview.People.Where(p => p.State == AccountStateDto.Active),
            PeopleFilter.Pending => Overview.People.Where(p => p.State == AccountStateDto.Invited),
            PeopleFilter.Suspended => Overview.People.Where(p => p.State is AccountStateDto.Suspended or AccountStateDto.Locked or AccountStateDto.Disabled),
            _ => Overview.People,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return people;
        }

        string needle = text.Trim();
        return people.Where(p =>
            NameOf(p).Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || (p.Email?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || p.Assignments.Any(a => RoleName(a.Role).Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            || p.TeamIds.Any(t => Team(t)?.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) == true));
    }
}

public enum PeopleFilter
{
    All,
    Active,
    Pending,
    Suspended,
}
