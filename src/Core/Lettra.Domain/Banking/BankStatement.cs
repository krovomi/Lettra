using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lettra.Domain.Common;

namespace Lettra.Domain.Banking;

/// <summary>Format d'un relevé importé (valeurs identiques à <c>BankStatementFormatDto</c>).</summary>
public enum BankStatementFormat
{
    /// <summary>ISO 20022 <c>camt.053</c>, le relevé de fin de journée que fournissent les banques françaises.</summary>
    Camt053 = 1,

    /// <summary>Open Financial Exchange (OFX 1.x SGML ou 2.x XML).</summary>
    Ofx = 2,

    /// <summary>Fichier à séparateurs, colonnes paramétrables.</summary>
    Csv = 3,
}

/// <summary>État d'une ligne du relevé (valeurs identiques à <c>BankLineStatusDto</c>).</summary>
public enum BankLineStatus
{
    /// <summary>À rapprocher.</summary>
    Pending = 1,

    /// <summary>Rapprochée : un encaissement a été enregistré à partir de la ligne.</summary>
    Matched = 2,

    /// <summary>Écartée par l'utilisateur (frais bancaires, virement interne, opération sans facture client).</summary>
    Ignored = 3,
}

/// <summary>Une opération lue dans un relevé, avant enregistrement : montant signé (crédit positif).</summary>
public sealed record BankTransaction(
    DateOnly BookingDate,
    decimal Amount,
    string Label,
    string? Reference,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? BankReference);

/// <summary>Un relevé lu : compte, période, opérations.</summary>
public sealed record ParsedBankStatement(
    BankStatementFormat Format,
    string? AccountIban,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<BankTransaction> Transactions);

/// <summary>Un import de relevé : fichier, compte, période, comptes rendus.</summary>
public sealed class BankStatementImport : Entity<Guid>
{
    public const int MaxFileNameLength = 200;

    private BankStatementImport(Guid id)
        : base(id)
    {
        FileName = string.Empty;
    }

    public string FileName { get; private set; }

    public BankStatementFormat Format { get; private set; }

    /// <summary>Compte du relevé, masqué (<c>FR76 •••• 1234</c>, ou <c>•••• 1234</c> pour un numéro de compte) : il n'est pas conservé en entier.</summary>
    public string? AccountDisplay { get; private set; }

    public DateOnly? From { get; private set; }

    public DateOnly? To { get; private set; }

    public int Imported { get; private set; }

    public int Duplicates { get; private set; }

    public Guid ImportedBy { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public static BankStatementImport Create(
        Guid id, string fileName, ParsedBankStatement statement, int imported, int duplicates, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(statement);
        string name = Path.GetFileName(fileName ?? string.Empty).Trim();
        return new BankStatementImport(id)
        {
            FileName = name.Length > MaxFileNameLength ? name[..MaxFileNameLength] : name,
            Format = statement.Format,
            AccountDisplay = BankText.MaskIban(statement.AccountIban) ?? BankText.MaskAccount(statement.AccountIban),
            From = statement.From ?? (statement.Transactions.Count == 0 ? null : statement.Transactions.Min(t => t.BookingDate)),
            To = statement.To ?? (statement.Transactions.Count == 0 ? null : statement.Transactions.Max(t => t.BookingDate)),
            Imported = imported,
            Duplicates = duplicates,
            ImportedBy = userId,
            ImportedAt = now,
        };
    }
}

/// <summary>
/// Une opération du relevé bancaire du dossier. L'empreinte (compte, date, montant, libellé, référence, rang parmi les
/// opérations identiques du fichier) empêche qu'un relevé importé deux fois crée des doublons. L'IBAN de la contrepartie
/// n'est gardé que masqué, avec son empreinte pour reconnaître un client déjà vu.
/// </summary>
public sealed class BankStatementLine : AggregateRoot<Guid>, IVersioned
{
    public const int MaxLabelLength = 500;
    public const int MaxReferenceLength = 140;
    public const int MaxNameLength = 140;

    private BankStatementLine(Guid id)
        : base(id)
    {
        Label = string.Empty;
        Fingerprint = string.Empty;
    }

    public Guid ImportId { get; private set; }

    public DateOnly BookingDate { get; private set; }

    /// <summary>Montant signé : positif pour un crédit (argent reçu), négatif pour un débit.</summary>
    public decimal Amount { get; private set; }

    public string Label { get; private set; }

    public string? Reference { get; private set; }

    public string? CounterpartyName { get; private set; }

    public string? CounterpartyIbanDisplay { get; private set; }

    /// <summary>Empreinte de l'IBAN de la contrepartie (<see cref="BankText.IbanFingerprint"/>), pour la règle « IBAN déjà connu ».</summary>
    public string? CounterpartyIbanHash { get; private set; }

    public string Fingerprint { get; private set; }

    public BankLineStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public DateTimeOffset? ChangedAt { get; private set; }

    public long Version { get; private set; }

    public bool IsCredit => Amount > 0m;

    public static BankStatementLine Create(Guid id, Guid importId, BankTransaction transaction, string fingerprint, string? ibanHash)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        return new BankStatementLine(id)
        {
            ImportId = importId,
            BookingDate = transaction.BookingDate,
            Amount = transaction.Amount,
            Label = BankText.Clip(transaction.Label, MaxLabelLength) ?? string.Empty,
            Reference = BankText.Clip(transaction.Reference, MaxReferenceLength),
            CounterpartyName = BankText.Clip(transaction.CounterpartyName, MaxNameLength),
            CounterpartyIbanDisplay = BankText.MaskIban(transaction.CounterpartyIban),
            CounterpartyIbanHash = ibanHash,
            Fingerprint = fingerprint,
            Status = BankLineStatus.Pending,
            Version = 1,
        };
    }

    /// <summary>Un encaissement vient d'être enregistré à partir de cette ligne.</summary>
    public Result MatchTo(PaymentId payment, Guid userId, DateTimeOffset now)
    {
        if (Status == BankLineStatus.Matched)
        {
            return BankStatementErrors.LineAlreadyMatched;
        }

        if (!IsCredit)
        {
            return BankStatementErrors.LineNotCredit;
        }

        Status = BankLineStatus.Matched;
        PaymentId = payment.Value;
        Changed(userId, now);
        return Result.Success();
    }

    /// <summary>Écarte la ligne (ou la remet à rapprocher). Une ligne rapprochée se défait par la contre-passation de son encaissement.</summary>
    public Result SetIgnored(bool ignored, Guid userId, DateTimeOffset now)
    {
        if (Status == BankLineStatus.Matched)
        {
            return BankStatementErrors.LineAlreadyMatched;
        }

        Status = ignored ? BankLineStatus.Ignored : BankLineStatus.Pending;
        Changed(userId, now);
        return Result.Success();
    }

    /// <summary>L'encaissement de la ligne a été contre-passé : elle redevient à rapprocher.</summary>
    public void Unmatch(Guid userId, DateTimeOffset now)
    {
        Status = BankLineStatus.Pending;
        PaymentId = null;
        Changed(userId, now);
    }

    private void Changed(Guid userId, DateTimeOffset now)
    {
        ChangedBy = userId;
        ChangedAt = now;
        Version++;
    }
}

/// <summary>Règles de texte communes aux relevés : IBAN masqué et empreinte, empreinte d'une opération, libellés.</summary>
public static class BankText
{
    /// <summary>IBAN sans espaces, en majuscules ; <c>null</c> s'il est vide.</summary>
    public static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        string compact = new([.. iban.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
        return compact.Length is >= 14 and <= 34 ? compact : null;
    }

    /// <summary>IBAN masqué pour l'affichage : pays et clé, puis les quatre derniers caractères.</summary>
    public static string? MaskIban(string? iban) =>
        NormalizeIban(iban) is { } compact ? $"{compact[..4]} •••• {compact[^4..]}" : null;

    /// <summary>Numéro de compte (non IBAN) masqué : les quatre derniers caractères.</summary>
    public static string? MaskAccount(string? account) =>
        AccountKey(account) is { Length: >= 4 } key ? $"•••• {key[^4..]}" : null;

    /// <summary>
    /// Empreinte d'un IBAN propre au dossier (SHA-256 de l'identifiant du dossier et de l'IBAN normalisé) : elle
    /// reconnaît un payeur déjà vu sans garder son IBAN en clair.
    /// </summary>
    public static string? IbanFingerprint(string? iban, Guid dossierId) =>
        NormalizeIban(iban) is { } compact
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"lettra-iban|{dossierId:D}|{compact}")))
            : null;

    /// <summary>Empreinte d'une opération, pour écarter les doublons d'un relevé importé deux fois.</summary>
    public static string TransactionFingerprint(string? accountIban, BankTransaction transaction, int occurrence)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        string text = string.Join(
            '|',
            AccountKey(accountIban),
            transaction.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Collapse(transaction.Label),
            Collapse(transaction.Reference),
            Collapse(transaction.BankReference),
            occurrence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Clé du compte du relevé pour l'empreinte : IBAN, ou numéro de compte d'un OFX, en lettres et chiffres.</summary>
    public static string AccountKey(string? account) =>
        string.IsNullOrWhiteSpace(account) ? string.Empty : new string([.. account.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    public static string? Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > max ? collapsed[..max] : collapsed;
    }

    private static string Collapse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
