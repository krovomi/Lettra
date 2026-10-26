using Lettra.Domain.Missions;

namespace Lettra.Application.Abstractions.Cabinet;

/// <summary>La mission et les temps passés, dans la base du dossier : ils suivent le dossier (export, sauvegarde).</summary>
public interface IMissionStore
{
    /// <summary>La mission suivie (modifiable), ou <c>null</c> tant qu'elle n'est pas saisie.</summary>
    Task<Mission?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(Mission mission, CancellationToken cancellationToken);

    /// <summary>Les temps passés entre deux dates incluses, les plus récents d'abord.</summary>
    Task<IReadOnlyList<TimeEntry>> ListTimeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<TimeEntry?> FindTimeAsync(Guid id, CancellationToken cancellationToken);

    Task AddTimeAsync(TimeEntry entry, CancellationToken cancellationToken);

    Task RemoveTimeAsync(TimeEntry entry, CancellationToken cancellationToken);
}

/// <summary>Le nom du cabinet et de ses membres, lus dans son registre ; vides sur un hôte sans registre (poste isolé).</summary>
public sealed record CabinetPeople(string? CabinetName, IReadOnlyDictionary<Guid, string> Names)
{
    public static CabinetPeople None { get; } = new(null, new Dictionary<Guid, string>());
}
