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
}

/// <summary>Archive scellée d'une clôture : un ZIP autoportant, vérifiable hors de Lettra avec la clé publique jointe.</summary>
public sealed record SealedArchive(string FileName, byte[] Content);

public interface ISealedArchiveBuilder
{
    Task<Result<SealedArchive>> BuildAsync(int closingNumber, CancellationToken cancellationToken);
}

/// <summary>Clés de la chaîne d'un cabinet : sceau des entrées (HMAC) et signature des clôtures (ECDSA P-256).</summary>
public interface IIntegrityKeys : IIntegrityKeyVerifier
{
    string KeyId { get; }

    /// <summary>Clé publique de signature (SubjectPublicKeyInfo, base 64).</summary>
    string PublicKey { get; }

    string Seal(string entryHash);

    string Sign(string closingHash);
}

/// <summary>
/// Dérive les clés de la chaîne de la clé maîtresse (KEK) du cabinet, protégée comme les clés des dossiers
/// (ADR-0011). Rien n'est stocké en base : qui n'a pas la KEK ne peut ni sceller ni signer.
/// </summary>
public interface IIntegrityKeyring
{
    ValueTask<Result<IIntegrityKeys>> GetAsync(OrganisationId cabinet, CancellationToken cancellationToken);
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
