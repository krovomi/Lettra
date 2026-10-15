using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Integrity;

/// <summary>
/// Journal inaltérable du dossier (chaîne anti-fraude, « IJournalInalterable » du plan). Appelé par le cas d'usage
/// de validation ou d'émission d'une pièce, DANS sa transaction (le <c>work</c> de
/// <c>IDocumentNumbering.ExecuteWithNextNumberAsync</c>), avant l'enregistrement : l'entrée et la pièce sont
/// enregistrées ensemble ou pas du tout. Sans transaction ouverte, le journal ouvre la sienne et enregistre.
/// </summary>
public interface IIntegrityJournal
{
    /// <summary>Ajoute l'événement en tête de chaîne ; renvoie son numéro d'entrée.</summary>
    Task<Result<long>> AppendAsync(DocumentLifecycleEvent @event, CancellationToken cancellationToken);
}

/// <summary>
/// Fourni par le module des pièces : recalcule, depuis la base, l'empreinte du contenu figé de chaque pièce
/// (<see cref="DocumentFingerprint"/>). Le contrôle d'intégrité la compare à celle du journal.
/// </summary>
public interface IJournaledDocumentReader
{
    /// <summary>Faux tant que le module des pièces n'est pas branché : les pièces ne sont alors pas comparées.</summary>
    bool IsConnected => true;

    /// <summary>Empreinte actuelle de chaque pièce trouvée ; une pièce absente du résultat est réputée supprimée.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);
}

/// <summary>
/// Fourni par le module Exports : l'empreinte (<see cref="PlatformEventFingerprint"/>) de chaque statut de chaque
/// transmission à la plateforme agréée enregistrée dans le dossier, dans l'ordre. Le contrôle d'intégrité la compare
/// au journal.
/// </summary>
public interface IPlatformHistoryReader
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ComputeFingerprintsAsync(CancellationToken cancellationToken);
}

/// <summary>Lecture du journal et des clôtures du dossier courant, et clôture d'une période.</summary>
public interface IIntegrityStore
{
    Task<IReadOnlyList<PeriodClosing>> ListClosingsAsync(CancellationToken cancellationToken);

    /// <summary>Toutes les entrées, dans l'ordre, lues par paquets.</summary>
    IAsyncEnumerable<JournalEntry> ReadEntriesAsync(CancellationToken cancellationToken);

    Task<DateTimeOffset?> FirstEntryAtAsync(CancellationToken cancellationToken);

    /// <summary>Prochain numéro de chaque série du dossier.</summary>
    Task<IReadOnlyDictionary<string, long>> NextNumbersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Clôture une période : sous le verrou de la tête de chaîne, couvre les entrées enregistrées avant sa fin et
    /// après la clôture précédente, signe, et inscrit la clôture dans la chaîne. Le tout en une transaction.
    /// </summary>
    Task<Result<PeriodClosing>> CloseAsync(ClosingPeriod period, CancellationToken cancellationToken);

    /// <summary>
    /// Rotation des clés : crée la version suivante et l'annonce dans la chaîne par une entrée scellée avec la clé
    /// sortante, sous le verrou de la tête de chaîne. L'ancienne version reste au registre, en vérification seule.
    /// </summary>
    Task<Result<IntegrityKeyVersion>> RotateKeysAsync(CancellationToken cancellationToken);
}

/// <summary>Archive scellée d'une clôture : un ZIP autoportant, vérifiable hors de Lettra avec la clé publique jointe.</summary>
public sealed record SealedArchive(string FileName, byte[] Content);

public interface ISealedArchiveBuilder
{
    Task<Result<SealedArchive>> BuildAsync(int closingNumber, CancellationToken cancellationToken);
}

/// <summary>
/// Une version de clé du registre des clés publiques d'un dossier. Elle est en vigueur de sa création à celle de la
/// suivante (<see cref="RetiredAt"/>), puis reste au registre, en vérification seule, toute la durée légale.
/// </summary>
public sealed record IntegrityKeyVersion(int Version, string KeyId, string PublicKey, DateTimeOffset CreatedAt, DateTimeOffset? RetiredAt);

/// <summary>
/// Clés de la chaîne du dossier courant (ADR-0011) : une clé de sceau (HMAC) et une clé de signature (ECDSA P-256)
/// par version, tirées au hasard et conservées dans la base du dossier enveloppées par la clé maîtresse (KEK) du
/// cabinet. Qui n'a pas la KEK ne peut ni sceller, ni signer, ni ajouter une version au registre.
/// </summary>
public interface IIntegrityKeyring
{
    /// <summary>Toutes les versions, retirées comprises, pour vérifier tout l'historique.</summary>
    Task<Result<IIntegrityKeyVerifier>> VerifierAsync(CancellationToken cancellationToken);

    /// <summary>Le registre des clés publiques, de la plus ancienne à la plus récente.</summary>
    Task<IReadOnlyList<IntegrityKeyVersion>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Les deux règles d'alerte de dossier de la chaîne (annexe G §5 et §6).</summary>
public enum DossierAlertRule
{
    /// <summary>Écart dans la chaîne anti-fraude : S1, conformité.</summary>
    IntegrityChainCompromised = 1,

    /// <summary>Trou dans la numérotation d'une série : S2, conformité.</summary>
    NumberingGap = 2,
}

/// <summary>
/// Un signal d'alerte de dossier : la règle, la portée en identifiants techniques, et si la condition est vraie
/// (<paramref name="Firing"/>) ou vient de disparaître. Aucune donnée métier.
/// </summary>
public sealed record DossierAlertSignal(DossierAlertRule Rule, OrganisationId Cabinet, DossierId Dossier, bool Firing, DateTimeOffset At);

/// <summary>Port vers le module Supervision, qui route l'alerte vers les bons rôles. Le cœur ne connaît pas le module.</summary>
public interface IDossierAlertPublisher
{
    Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken);
}
