using Lettra.Application.Abstractions.Identity;
using Lettra.UI.Abstractions;

namespace Lettra.DemoData;

/// <summary>
/// L'horloge du chargement : elle donne l'instant de l'étape rejouée (une pièce de mars est créée « en mars »), pour
/// que dates, numéros et chaîne d'intégrité gardent l'ordre chronologique. Elle ne sert qu'au conteneur du chargement,
/// jamais à l'application en cours d'exécution.
/// </summary>
internal sealed class DemoClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = now.ToUniversalTime();
}

/// <summary>
/// Qui charge et dans quel dossier : l'utilisateur de la démonstration (ses droits viennent du registre, comme pour
/// tout utilisateur) et le dossier en cours de remplissage.
/// </summary>
internal sealed class DemoSession(Guid userId) : ICurrentUser, IDossierSelection
{
    public bool IsAuthenticated => userId != Guid.Empty;

    public Guid UserId => userId;

    public Guid? CurrentDossierId { get; set; }
}
