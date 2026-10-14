namespace Lettra.Domain;

/// Ligne unique écrite à la création de la base : « cette base appartient au dossier X ».
/// La fabrique de DbContext la relit avant toute requête et refuse une base qui ne correspond pas
/// au dossier actif (garde-fou contre une erreur de routage dans le catalogue ou PgBouncer).
public sealed class DossierIdentity
{
    private DossierIdentity() { }

    public DossierIdentity(Guid dossierId, Guid organisationId)
    {
        DossierId = dossierId;
        OrganisationId = organisationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; } = 1;
    public Guid DossierId { get; private set; }
    public Guid OrganisationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
