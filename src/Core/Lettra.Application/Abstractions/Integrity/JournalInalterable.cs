using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Integrity;

/// <summary>
/// Journal inaltérable du dossier (chaîne anti-fraude, art. 286 I-3° bis CGI) : chaque entrée est chaînée en SHA-256 à
/// la précédente. Port fourni par le fil « Chaîne anti-fraude et alertes dossier » ; la validation d'une pièce l'appelle
/// dans sa transaction, si bien que le numéro, la pièce figée et l'entrée s'écrivent ensemble ou pas du tout.
/// </summary>
public interface IJournalInalterable
{
    Task<Result> AjouterAsync(EntreeJournal entree, CancellationToken cancellationToken);
}

/// <summary>Une entrée du journal : l'événement, la pièce, son empreinte canonique, l'auteur, l'heure.</summary>
/// <param name="TypeEvenement">« FactureValidee », « AvoirValide », « FactureEmise », « DevisValide ».</param>
/// <param name="DocumentId">Identifiant de la pièce.</param>
/// <param name="Numero">Numéro de la pièce.</param>
/// <param name="EmpreinteDocument">SHA-256 hexadécimal minuscule de la forme canonique (<c>CanonicalDocument</c>).</param>
/// <param name="UtilisateurId">Auteur de l'action.</param>
/// <param name="Horodatage">Heure de l'action.</param>
public sealed record EntreeJournal(
    string TypeEvenement,
    Guid DocumentId,
    string? Numero,
    string EmpreinteDocument,
    Guid UtilisateurId,
    DateTimeOffset Horodatage);
