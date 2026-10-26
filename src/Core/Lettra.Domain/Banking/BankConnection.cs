using Lettra.Domain.Common;

namespace Lettra.Domain.Banking;

/// <summary>État d'une banque connectée (valeurs identiques à <c>BankConnectionStatusDto</c>, sauf « expirée », calculée).</summary>
public enum BankConnectionStatus
{
    /// <summary>Consentement demandé, pas encore donné à la banque.</summary>
    Pending = 1,

    /// <summary>Consentement donné : les opérations du compte peuvent être lues jusqu'à son échéance.</summary>
    Active = 2,

    /// <summary>Déconnectée par le cabinet, ou remplacée par un renouvellement.</summary>
    Revoked = 3,
}

/// <summary>
/// Un compte bancaire du dossier relié par un agrégateur (DSP2) : le cabinet consent, sur la page de la banque, à ce
/// que Lettra lise les opérations du compte pendant une durée limitée (180 jours en général). Lettra ne garde que le
/// compte masqué et la référence de l'agrégateur ; les opérations lues deviennent des lignes de relevé comme un
/// fichier importé. Une seule synchronisation à la fois : elle est réservée avant de lire la banque.
/// </summary>
public sealed class BankConnection : Entity<Guid>, IVersioned
{
    public const int MaxProviderLength = 40;
    public const int MaxInstitutionIdLength = 60;
    public const int MaxInstitutionNameLength = 120;
    public const int MaxReferenceLength = 100;

    /// <summary>Le consentement est à renouveler dans les 15 derniers jours.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(15);

    /// <summary>À l'ouverture du rapprochement, un compte relu il y a plus de 6 heures est relu.</summary>
    public static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);

    /// <summary>Une synchronisation réservée et jamais terminée (processus arrêté) n'en bloque pas d'autre au-delà.</summary>
    public static readonly TimeSpan SyncLease = TimeSpan.FromMinutes(2);

    private BankConnection(Guid id)
        : base(id)
    {
    }

    public string Provider { get; private init; } = "";

    public string InstitutionId { get; private init; } = "";

    public string InstitutionName { get; private init; } = "";

    /// <summary>Référence du consentement chez l'agrégateur ; c'est elle qui permet de lire le compte.</summary>
    public string ProviderReference { get; private init; } = "";

    /// <summary>Compte masqué (<c>FR76 •••• 1234</c>), connu une fois le consentement donné.</summary>
    public string? AccountDisplay { get; private set; }

    public BankConnectionStatus Status { get; private set; }

    public DateTimeOffset? ConsentExpiresAt { get; private set; }

    /// <summary>La connexion que celle-ci renouvelle : elle est déconnectée quand le nouveau consentement est donné.</summary>
    public Guid? ReplacesId { get; private init; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? LastSyncAt { get; private set; }

    /// <summary>Opérations nouvelles à la dernière synchronisation.</summary>
    public int LastSyncImported { get; private set; }

    public DateTimeOffset? SyncStartedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public long Version { get; private set; }

    /// <summary>Le consentement est demandé ; <paramref name="id"/> a déjà été confié à l'agrégateur, qui le rend au retour.</summary>
    public static BankConnection Start(
        string provider, string institutionId, string institutionName, string providerReference, Guid? replaces, Guid userId, DateTimeOffset now, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        return new BankConnection(id)
        {
            Provider = Clip(provider, MaxProviderLength),
            InstitutionId = Clip(institutionId, MaxInstitutionIdLength),
            InstitutionName = Clip(institutionName, MaxInstitutionNameLength),
            ProviderReference = Clip(providerReference, MaxReferenceLength),
            ReplacesId = replaces,
            Status = BankConnectionStatus.Pending,
            CreatedBy = userId,
            CreatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Le consentement est donné : le compte (gardé masqué seulement) peut être lu jusqu'à <paramref name="consentExpiresAt"/>.</summary>
    public Result Activate(string? accountIban, DateTimeOffset consentExpiresAt, DateTimeOffset now)
    {
        if (Status != BankConnectionStatus.Pending)
        {
            return BankConnectionErrors.AlreadyCompleted;
        }

        if (consentExpiresAt <= now)
        {
            return BankConnectionErrors.ConsentExpired;
        }

        AccountDisplay = BankText.MaskIban(accountIban) ?? BankText.MaskAccount(accountIban);
        ConsentExpiresAt = consentExpiresAt;
        ActivatedAt = now;
        Status = BankConnectionStatus.Active;
        Version++;
        return Result.Success();
    }

    public bool IsActive(DateTimeOffset now) => Status == BankConnectionStatus.Active && ConsentExpiresAt > now;

    public bool IsExpired(DateTimeOffset now) => Status == BankConnectionStatus.Active && ConsentExpiresAt <= now;

    /// <summary>Consentement échu ou proche de l'être : le cabinet doit repasser par la banque.</summary>
    public bool NeedsRenewal(DateTimeOffset now) => Status == BankConnectionStatus.Active && ConsentExpiresAt - now <= RenewalWindow;

    public bool IsSyncDue(DateTimeOffset now) => IsActive(now) && (LastSyncAt is null || now - LastSyncAt >= SyncInterval);

    /// <summary>Réserve la synchronisation : une autre, déjà en cours, la refuse.</summary>
    public Result BeginSync(DateTimeOffset now)
    {
        if (IsExpired(now))
        {
            return BankConnectionErrors.ConsentExpired;
        }

        if (!IsActive(now))
        {
            return BankConnectionErrors.NotActive;
        }

        if (SyncStartedAt is { } started && now - started < SyncLease)
        {
            return BankConnectionErrors.SyncInProgress;
        }

        SyncStartedAt = now;
        Version++;
        return Result.Success();
    }

    public void Synced(int imported, DateTimeOffset now)
    {
        LastSyncAt = now;
        LastSyncImported = imported;
        SyncStartedAt = null;
        Version++;
    }

    /// <summary>La banque n'a pas répondu : la réservation est rendue, la prochaine synchronisation réessaiera.</summary>
    public void SyncFailed()
    {
        SyncStartedAt = null;
        Version++;
    }

    public Result Revoke(Guid userId, DateTimeOffset now)
    {
        if (Status == BankConnectionStatus.Revoked)
        {
            return BankConnectionErrors.NotActive;
        }

        Status = BankConnectionStatus.Revoked;
        RevokedAt = now;
        RevokedBy = userId;
        SyncStartedAt = null;
        Version++;
        return Result.Success();
    }

    private static string Clip(string value, int max)
    {
        string trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

/// <summary>Codes d'erreur des banques connectées.</summary>
public static class BankConnectionErrors
{
    public const string UnavailableCode = "BankConnection.Unavailable";
    public const string InstitutionUnknownCode = "BankConnection.InstitutionUnknown";
    public const string NotFoundCode = "BankConnection.NotFound";
    public const string NotActiveCode = "BankConnection.NotActive";
    public const string AlreadyCompletedCode = "BankConnection.AlreadyCompleted";
    public const string ConsentRefusedCode = "BankConnection.ConsentRefused";
    public const string ConsentExpiredCode = "BankConnection.ConsentExpired";
    public const string SyncInProgressCode = "BankConnection.SyncInProgress";
    public const string BankUnreachableCode = "BankConnection.BankUnreachable";

    /// <summary>Aucun agrégateur bancaire n'est configuré sur cette installation.</summary>
    public static Error Unavailable => Error.Unavailable(UnavailableCode);

    public static Error InstitutionUnknown => Error.Validation(InstitutionUnknownCode, "InstitutionId");

    public static Error NotFound => Error.NotFound(NotFoundCode);

    /// <summary>La connexion n'est pas (ou plus) active : consentement pas encore donné, ou déconnectée.</summary>
    public static Error NotActive => Error.Conflict(NotActiveCode);

    public static Error AlreadyCompleted => Error.Conflict(AlreadyCompletedCode);

    /// <summary>Le consentement a été refusé ou abandonné sur la page de la banque.</summary>
    public static Error ConsentRefused => Error.Conflict(ConsentRefusedCode);

    /// <summary>Le consentement est échu : il faut le renouveler.</summary>
    public static Error ConsentExpired => Error.Conflict(ConsentExpiredCode);

    public static Error SyncInProgress => Error.Conflict(SyncInProgressCode);

    /// <summary>La banque ou l'agrégateur n'a pas répondu.</summary>
    public static Error BankUnreachable => Error.Unavailable(BankUnreachableCode);
}
