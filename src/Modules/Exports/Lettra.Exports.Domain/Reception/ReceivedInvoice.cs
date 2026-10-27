using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Domain.Reception;

/// <summary>Nature d'une facture fournisseur reçue, lue dans son code de type (BT-3).</summary>
public enum ReceivedInvoiceKind
{
    /// <summary>Facture (380).</summary>
    Invoice = 1,

    /// <summary>Facture d'acompte (386).</summary>
    DownPayment = 2,

    /// <summary>Avoir (381).</summary>
    CreditNote = 3,
}

/// <summary>Ce que la facture électronique reçue porte, lu dans son XML (CII ou UBL).</summary>
public sealed record ReceivedInvoiceHeader(
    ReceivedInvoiceKind Kind,
    string Number,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string SupplierName,
    string? SupplierSiren,
    string? SupplierVatNumber,
    string BuyerSiren,
    string Currency,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string? BusinessProcess);

/// <summary>
/// Une facture fournisseur reçue par la plateforme agréée de l'entreprise (réception obligatoire pour toutes les
/// entreprises au 1er septembre 2026). Elle arrive « Mise à disposition » ; l'entreprise, en acheteur, y répond par les
/// statuts du cycle de vie (XP Z12-012) : prise en charge, approuvée, en litige, refusée (définitif), paiement transmis.
/// Le fichier reçu est gardé tel quel : c'est l'original de la facture. Les statuts ne s'effacent ni ne se réécrivent.
/// </summary>
public sealed class ReceivedInvoice
{
    /// <summary>Longueur maximale d'un motif de litige ou de refus.</summary>
    public const int MaxReasonLength = 500;

    private readonly List<PlatformStatusEvent> _history;

    public ReceivedInvoice(
        Guid id,
        string platform,
        string reference,
        EInvoiceFormat format,
        string fileName,
        ReadOnlyMemory<byte> content,
        ReceivedInvoiceHeader header,
        Guid? supplierId,
        IEnumerable<PlatformStatusEvent> history)
    {
        ArgumentNullException.ThrowIfNull(header);
        Id = id;
        Platform = platform;
        Reference = reference;
        Format = format;
        FileName = fileName;
        Content = content;
        Header = header;
        SupplierId = supplierId;
        _history = [.. history];
        if (_history.Count == 0)
        {
            throw new ArgumentException("Une facture reçue porte au moins son statut de mise à disposition.", nameof(history));
        }
    }

    public Guid Id { get; }

    public string Platform { get; }

    /// <summary>Référence de la facture à la plateforme : elle identifie la facture dans chaque réponse.</summary>
    public string Reference { get; }

    public EInvoiceFormat Format { get; }

    public string FileName { get; }

    /// <summary>Le fichier reçu, octet pour octet.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    public ReceivedInvoiceHeader Header { get; }

    /// <summary>Le fournisseur du dossier qui porte ce SIREN, s'il y en a un.</summary>
    public Guid? SupplierId { get; }

    public IReadOnlyList<PlatformStatusEvent> History => _history;

    public PlatformStatus Status => _history[^1].Status;

    public DateTimeOffset ReceivedAt => _history[0].At;

    /// <summary>Refusée : l'acheteur ne répond plus, le fournisseur corrige par une nouvelle facture.</summary>
    public bool IsFinal => PlatformLifecycle.IsTerminal(Status);

    /// <summary>Les réponses que l'acheteur peut donner ici, depuis le statut actuel.</summary>
    public IReadOnlyList<PlatformStatus> AvailableResponses =>
        [.. BuyerResponses.Where(s => PlatformLifecycle.CanMove(Status, s) && (s != PlatformStatus.PaymentSent || Header.Kind != ReceivedInvoiceKind.CreditNote))];

    /// <summary>Les statuts qu'un acheteur pose sur une facture reçue.</summary>
    public static IReadOnlyList<PlatformStatus> BuyerResponses { get; } =
        [PlatformStatus.InProgress, PlatformStatus.Approved, PlatformStatus.Disputed, PlatformStatus.Refused, PlatformStatus.PaymentSent];

    /// <summary>Un litige ou un refus se motive : le fournisseur le lit à sa plateforme.</summary>
    public static bool RequiresReason(PlatformStatus status) => status is PlatformStatus.Disputed or PlatformStatus.Refused;

    /// <summary>Ajoute la réponse de l'acheteur, que la plateforme a acceptée.</summary>
    /// <exception cref="InvalidOperationException">Réponse impossible depuis le statut actuel.</exception>
    public void Apply(PlatformStatusEvent response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!AvailableResponses.Contains(response.Status))
        {
            throw new InvalidOperationException($"Statut {(int)response.Status} impossible depuis {(int)Status}.");
        }

        _history.Add(response);
    }
}
