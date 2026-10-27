using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Une plateforme agréée simulée, en mémoire (<c>Lettra:EInvoicing:Platform=Simulated</c>) : pour la démonstration et
/// les tests, en attendant la PA partenaire (5.5). Elle fait les contrôles d'une vraie plateforme qui ne demandent pas
/// de réseau : syntaxe annoncée, numéro du fichier, doublon, destinataire présent à l'annuaire. Une facture déposée
/// passe Émise, Reçue puis Mise à disposition à la première lecture de ses statuts ; les réponses de l'acheteur se
/// simulent par <see cref="SimulateBuyerStatus"/>. Côté acheteur, elle met à disposition de chaque SIREN quatre factures
/// fournisseurs fictives (<see cref="SimulatedSupplierInvoices"/>) et enregistre ses réponses. Elle tient aussi
/// l'annuaire : tout SIREN valide y figure, à son adresse 0225, sauf ceux retirés par <see cref="Unregister"/>.
/// Données fictives seulement : rien ne sort du processus.
/// </summary>
public sealed class SimulatedAccreditedPlatform(TimeProvider clock) : IAccreditedPlatformClient, IEInvoicingDirectory
{
    /// <summary>Date d'inscription des entreprises à l'annuaire simulé.</summary>
    public static readonly DateOnly DirectorySince = new(2026, 6, 1);

    public const string PlatformName = "PA simulée";

    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace UblInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace UblCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static readonly PlatformStatus[] Routing = [PlatformStatus.Issued, PlatformStatus.Received, PlatformStatus.MadeAvailable];

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Deposit> _deposits = [];
    private readonly HashSet<string> _unregistered = [];

    // Envois déjà traités, par clé d'idempotence : un envoi repris rend la réponse du premier.
    private readonly Dictionary<string, PlatformReceipt> _receiptsByKey = [];
    private readonly Dictionary<string, PlatformStatusEvent> _paymentsByKey = [];

    // Factures fournisseurs mises à disposition, par acheteur, et l'historique de chacune par référence.
    private readonly Dictionary<string, List<PlatformIncomingInvoice>> _incoming = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PlatformStatusEvent>> _incomingHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlatformStatusEvent> _responsesByKey = [];
    private int _sequence;

    public string Name => PlatformName;

    /// <summary>Plateforme injoignable : chaque appel échoue en <c>Platform.Unavailable</c>, rien n'est reçu.</summary>
    public bool IsUnavailable { get; set; }

    /// <summary>Retire un SIREN de l'annuaire : les factures qui lui sont adressées sont rejetées.</summary>
    public void Unregister(string siren)
    {
        lock (_lock)
        {
            _unregistered.Add(siren);
        }
    }

    public void Register(string siren)
    {
        lock (_lock)
        {
            _unregistered.Remove(siren);
        }
    }

    public Task<Result<PlatformReceipt>> SubmitInvoiceAsync(PlatformInvoiceSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (IsUnavailable)
        {
            return Task.FromResult<Result<PlatformReceipt>>(PlatformErrors.Unavailable);
        }

        string? rejection = Check(submission);
        lock (_lock)
        {
            if (submission.IdempotencyKey is string key && _receiptsByKey.TryGetValue(key, out PlatformReceipt? known))
            {
                return Task.FromResult<Result<PlatformReceipt>>(known);
            }

            if (rejection is null && _deposits.Values.Any(d => d.Accepted && d.SellerSiren == submission.SellerSiren && d.InvoiceNumber == submission.InvoiceNumber))
            {
                rejection = "Doublon : une facture de ce numéro a déjà été déposée par ce vendeur.";
            }

            Deposit deposit = Add(submission.SellerSiren, submission.InvoiceNumber, rejection is null, rejection is null, rejection);
            return Task.FromResult<Result<PlatformReceipt>>(Remember(submission.IdempotencyKey, new PlatformReceipt(deposit.Reference, deposit.History[0])));
        }
    }

    public Task<Result<PlatformReceipt>> ReportTransactionAsync(PlatformTransactionReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (IsUnavailable)
        {
            return Task.FromResult<Result<PlatformReceipt>>(PlatformErrors.Unavailable);
        }

        string? rejection = !IsSiren(report.SellerSiren) ? "SIREN du vendeur invalide."
            : report.TotalExclTax + report.TotalVat != report.TotalInclTax ? "Totaux incohérents : HT + TVA diffère du TTC."
            : null;
        lock (_lock)
        {
            if (report.IdempotencyKey is string key && _receiptsByKey.TryGetValue(key, out PlatformReceipt? known))
            {
                return Task.FromResult<Result<PlatformReceipt>>(known);
            }

            Deposit deposit = Add(report.SellerSiren, report.InvoiceNumber, rejection is null, routed: false, rejection);
            return Task.FromResult<Result<PlatformReceipt>>(Remember(report.IdempotencyKey, new PlatformReceipt(deposit.Reference, deposit.History[0])));
        }
    }

    public Task<Result<IReadOnlyList<PlatformStatusEvent>>> GetStatusEventsAsync(string reference, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            return Task.FromResult<Result<IReadOnlyList<PlatformStatusEvent>>>(PlatformErrors.Unavailable);
        }

        lock (_lock)
        {
            if (!_deposits.TryGetValue(reference, out Deposit? deposit))
            {
                return Task.FromResult<Result<IReadOnlyList<PlatformStatusEvent>>>(PlatformErrors.ReferenceUnknown);
            }

            if (deposit.Routed && deposit.History.Count == 1)
            {
                foreach (PlatformStatus status in Routing)
                {
                    deposit.History.Add(new PlatformStatusEvent(status, Now()));
                }
            }

            return Task.FromResult<Result<IReadOnlyList<PlatformStatusEvent>>>(deposit.History.ToList());
        }
    }

    public Task<Result<PlatformStatusEvent>> ReportPaymentAsync(PlatformPaymentReport payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (IsUnavailable)
        {
            return Task.FromResult<Result<PlatformStatusEvent>>(PlatformErrors.Unavailable);
        }

        lock (_lock)
        {
            if (payment.IdempotencyKey is string key && _paymentsByKey.TryGetValue(key, out PlatformStatusEvent? known))
            {
                return Task.FromResult<Result<PlatformStatusEvent>>(known);
            }

            if (!_deposits.TryGetValue(payment.Reference, out Deposit? deposit) || !deposit.Accepted)
            {
                return Task.FromResult<Result<PlatformStatusEvent>>(PlatformErrors.ReferenceUnknown);
            }

            PlatformStatusEvent collected = new(PlatformStatus.Collected, Now(), null, payment.Amount);
            deposit.History.Add(collected);
            if (payment.IdempotencyKey is string paid)
            {
                _paymentsByKey[paid] = collected;
            }

            return Task.FromResult<Result<PlatformStatusEvent>>(collected);
        }
    }

    public Task<Result<DirectoryEntry?>> LookupAsync(string siren, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            return Task.FromResult<Result<DirectoryEntry?>>(PlatformErrors.Unavailable);
        }

        lock (_lock)
        {
            DirectoryEntry? entry = IsSiren(siren) && !_unregistered.Contains(siren)
                ? new DirectoryEntry(siren, $"{EInvoiceDirectoryScheme}:{siren}", PlatformName, DirectorySince)
                : null;
            return Task.FromResult<Result<DirectoryEntry?>>(entry);
        }
    }

    public Task<Result<IReadOnlyList<PlatformIncomingInvoice>>> ListIncomingInvoicesAsync(string buyerSiren, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            return Task.FromResult<Result<IReadOnlyList<PlatformIncomingInvoice>>>(PlatformErrors.Unavailable);
        }

        lock (_lock)
        {
            return Task.FromResult<Result<IReadOnlyList<PlatformIncomingInvoice>>>(IncomingOf(buyerSiren).ToList());
        }
    }

    public Task<Result<PlatformStatusEvent>> RespondToIncomingInvoiceAsync(PlatformBuyerResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (IsUnavailable)
        {
            return Task.FromResult<Result<PlatformStatusEvent>>(PlatformErrors.Unavailable);
        }

        lock (_lock)
        {
            if (response.IdempotencyKey is string key && _responsesByKey.TryGetValue(key, out PlatformStatusEvent? known))
            {
                return Task.FromResult<Result<PlatformStatusEvent>>(known);
            }

            // Après un redémarrage, les factures de l'acheteur se reconstituent : leurs références sont stables.
            if (!IncomingOf(response.BuyerSiren).Any(i => i.Reference == response.Reference)
                || !_incomingHistory.TryGetValue(response.Reference, out List<PlatformStatusEvent>? history))
            {
                return Task.FromResult<Result<PlatformStatusEvent>>(PlatformErrors.ReferenceUnknown);
            }

            PlatformStatusEvent added = new(response.Status, Now(), response.Reason, response.Amount);
            history.Add(added);
            if (response.IdempotencyKey is string sent)
            {
                _responsesByKey[sent] = added;
            }

            return Task.FromResult<Result<PlatformStatusEvent>>(added);
        }
    }

    /// <summary>Met une facture de plus à disposition de cet acheteur (tests : fichier illisible, doublon, autre SIREN).</summary>
    public void DeliverIncoming(string buyerSiren, PlatformIncomingInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        lock (_lock)
        {
            IncomingOf(buyerSiren).Add(invoice);
            _incomingHistory.TryAdd(invoice.Reference, [new PlatformStatusEvent(PlatformStatus.MadeAvailable, invoice.MadeAvailableAt)]);
        }
    }

    /// <summary>Les réponses de l'acheteur reçues pour cette facture, dans l'ordre.</summary>
    public IReadOnlyList<PlatformStatusEvent> IncomingHistory(string reference)
    {
        lock (_lock)
        {
            return _incomingHistory.TryGetValue(reference, out List<PlatformStatusEvent>? history) ? history.ToList() : [];
        }
    }

    private const string EInvoiceDirectoryScheme = "0225";

    private List<PlatformIncomingInvoice> IncomingOf(string buyerSiren)
    {
        if (!_incoming.TryGetValue(buyerSiren, out List<PlatformIncomingInvoice>? invoices))
        {
            invoices = IsSiren(buyerSiren) && !_unregistered.Contains(buyerSiren)
                ? [.. SimulatedSupplierInvoices.For(buyerSiren, $"Entreprise {buyerSiren}", clock.GetUtcNow())]
                : [];
            _incoming[buyerSiren] = invoices;
            foreach (PlatformIncomingInvoice invoice in invoices)
            {
                _incomingHistory.TryAdd(invoice.Reference, [new PlatformStatusEvent(PlatformStatus.MadeAvailable, invoice.MadeAvailableAt)]);
            }
        }

        return invoices;
    }

    /// <summary>Une réponse de l'acheteur (prise en charge, approbation, litige, refus, paiement transmis…).</summary>
    /// <exception cref="InvalidOperationException">Référence inconnue, ou statut hors du cycle de vie.</exception>
    public PlatformStatusEvent SimulateBuyerStatus(string reference, PlatformStatus status, string? reason = null)
    {
        lock (_lock)
        {
            if (!_deposits.TryGetValue(reference, out Deposit? deposit))
            {
                throw new InvalidOperationException($"Référence inconnue de la PA simulée : {reference}.");
            }

            if (!PlatformLifecycle.CanMove(deposit.History[^1].Status, status) || PlatformLifecycle.IsDeclaredBySeller(status))
            {
                throw new InvalidOperationException($"Statut {(int)status} impossible depuis {(int)deposit.History[^1].Status}.");
            }

            PlatformStatusEvent added = new(status, Now(), reason);
            deposit.History.Add(added);
            return added;
        }
    }

    /// <summary>Dépôts reçus (acceptés ou rejetés), doublons compris : pour vérifier qu'un envoi repris n'en crée pas.</summary>
    public int DepositCount
    {
        get
        {
            lock (_lock)
            {
                return _deposits.Count;
            }
        }
    }

    private PlatformReceipt Remember(string? key, PlatformReceipt receipt)
    {
        if (key is not null)
        {
            _receiptsByKey[key] = receipt;
        }

        return receipt;
    }

    private string? Check(PlatformInvoiceSubmission submission)
    {
        if (!IsSiren(submission.SellerSiren))
        {
            return "SIREN du vendeur invalide.";
        }

        lock (_lock)
        {
            if (!IsSiren(submission.BuyerSiren) || _unregistered.Contains(submission.BuyerSiren))
            {
                return "Destinataire absent de l'annuaire : aucune plateforme de réception pour ce SIREN.";
            }
        }

        XDocument xml;
        try
        {
            using MemoryStream content = new(submission.Content.ToArray());
            using XmlReader reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            xml = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return "Fichier illisible : ce n'est pas un XML bien formé.";
        }

        string? number = submission.Format switch
        {
            EInvoiceFormatDto.Cii when xml.Root?.Name == Rsm + "CrossIndustryInvoice" =>
                xml.Root.Element(Rsm + "ExchangedDocument")?.Element(Ram + "ID")?.Value,
            EInvoiceFormatDto.Ubl when xml.Root?.Name == UblInvoice + "Invoice" || xml.Root?.Name == UblCreditNote + "CreditNote" =>
                xml.Root.Element(Cbc + "ID")?.Value,
            _ => null,
        };
        return number is null ? $"Syntaxe non reconnue : le fichier n'est pas une facture {submission.Format.ToString().ToUpperInvariant()}."
            : number != submission.InvoiceNumber ? "Le numéro de la facture dans le fichier diffère de celui déclaré."
            : null;
    }

    private Deposit Add(string sellerSiren, string invoiceNumber, bool accepted, bool routed, string? rejection)
    {
        string reference = string.Create(CultureInfo.InvariantCulture, $"PAS-{++_sequence:000000}");
        Deposit deposit = new(reference, sellerSiren, invoiceNumber, accepted, routed && accepted);
        deposit.History.Add(accepted
            ? new PlatformStatusEvent(PlatformStatus.Deposited, Now())
            : new PlatformStatusEvent(PlatformStatus.Rejected, Now(), rejection));
        _deposits[reference] = deposit;
        return deposit;
    }

    // À la milliseconde, comme les statuts enregistrés dans le dossier.
    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());

    private static bool IsSiren(string? siren) => siren is { Length: 9 } && siren.All(char.IsAsciiDigit);

    private sealed record Deposit(string Reference, string SellerSiren, string InvoiceNumber, bool Accepted, bool Routed)
    {
        public List<PlatformStatusEvent> History { get; } = [];
    }
}
