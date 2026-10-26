using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Agrégateur bancaire simulé, pour la démonstration : trois banques fictives, un consentement donné sur une page de
/// l'application (<see cref="BankConnectionRoutes.SimulatedConsent"/>) qui joue la banque, et des opérations fabriquées à partir des factures
/// du dossier, comme si une partie des clients avaient payé par virement. Tout est déduit de la référence de la
/// connexion (compte, références bancaires, dates) : deux lectures rendent les mêmes opérations, même après un
/// redémarrage, et l'import en écarte les doublons. Aucun compte réel, rien ne sort du processus.
/// </summary>
public sealed class SimulatedBankAggregator(IReceivableDocumentReader receivables, TimeProvider clock) : IBankAggregator
{
    public const string ProviderName = "Agrégateur simulé";

    /// <summary>Durée du consentement DSP2 (accès aux comptes) : 180 jours.</summary>
    public const int ConsentDays = 180;

    /// <summary>Frais mensuels de tenue de compte, débités le 5 de chaque mois.</summary>
    public const decimal MonthlyFee = 9.90m;

    private const string ReferencePrefix = "sim-";

    public static readonly IReadOnlyList<BankInstitution> Institutions =
    [
        new("sim-lumiere", "Banque Lumière (simulée)"),
        new("sim-horizon", "Crédit Horizon (simulé)"),
        new("sim-neo", "Néo Compte (simulé)"),
    ];

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<IReadOnlyList<BankInstitution>> ListInstitutionsAsync(CancellationToken cancellationToken) => Task.FromResult(Institutions);

    public Task<BankConsentStart> StartConsentAsync(BankInstitution institution, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institution);
        string redirect = string.Create(
            CultureInfo.InvariantCulture, $"{BankConnectionRoutes.SimulatedConsent}?connection={connectionId}&bank={Uri.EscapeDataString(institution.Name)}");
        return Task.FromResult(new BankConsentStart($"{ReferencePrefix}{connectionId:N}", redirect));
    }

    public Task<Result<BankConsentGrant>> CompleteConsentAsync(string providerReference, string? callbackCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerReference);
        Result<BankConsentGrant> result = providerReference.StartsWith(ReferencePrefix, StringComparison.Ordinal) && callbackCode == BankConnectionRoutes.SimulatedGrantedCode
            ? new BankConsentGrant(AccountIban(providerReference), clock.GetUtcNow().AddDays(ConsentDays))
            : BankConnectionErrors.ConsentRefused;
        return Task.FromResult(result);
    }

    public async Task<Result<ParsedBankStatement>> FetchAsync(string providerReference, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerReference);
        DateOnly today = LegalCalendar.Today(clock);
        DateOnly last = to > today ? today : to;
        List<BankTransaction> transactions = [];

        // Les factures que « le client a payées » : environ deux sur trois, le montant exact, quelques jours après leur
        // émission ; une sur deux cite son numéro. Une facture déjà réglée (même en partie) ou créditée n'est plus payée.
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(null, cancellationToken).ConfigureAwait(false);
        foreach (ReceivableDocument document in documents.Where(d => d.Kind != InvoiceKind.CreditNote && d.TotalInclTax > 0m && d.PaidInclTax == 0m && d.CreditedInclTax == 0m))
        {
            uint hash = Hash($"{providerReference}|{document.InvoiceId.Value:N}");
            DateOnly date = document.IssueDate.AddDays(3 + (int)(hash % 25));
            if (hash % 3 == 0 || date < from || date > last)
            {
                continue;
            }

            string name = document.CustomerName.ToUpperInvariant();
            bool citesNumber = hash % 2 == 0;
            transactions.Add(new BankTransaction(
                date,
                document.TotalInclTax,
                citesNumber ? $"VIR SEPA {name} FACTURE {document.Number}" : $"VIR {name} REGLEMENT",
                citesNumber ? document.Number : null,
                document.CustomerName,
                Iban($"payeur|{document.CustomerId.Value:N}"),
                string.Create(CultureInfo.InvariantCulture, $"SIM{hash:X8}")));
        }

        for (DateOnly month = new(from.Year, from.Month, 1); month <= last; month = month.AddMonths(1))
        {
            DateOnly date = month.AddDays(4);
            if (date >= from && date <= last)
            {
                transactions.Add(new BankTransaction(
                    date, -MonthlyFee, $"FRAIS TENUE DE COMPTE {date:MM/yyyy}", null, null, null, $"SIMFRAIS{date:yyyyMM}"));
            }
        }

        return new ParsedBankStatement(
            BankStatementFormat.Connection, AccountIban(providerReference), from, last, [.. transactions.OrderBy(t => t.BookingDate)]);
    }

    public Task RevokeAsync(string providerReference, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>L'IBAN fictif du compte relié (banque 99999, qui n'existe pas), clés RIB et IBAN justes.</summary>
    public static string AccountIban(string providerReference) => Iban($"compte|{providerReference}");

    private static string Iban(string seed)
    {
        const string Bank = "99999";
        const string Branch = "00001";
        string account = (Hash(seed) % 100_000_000_000UL).ToString("D11", CultureInfo.InvariantCulture);
        long ribKey = 97 - (((89 * long.Parse(Bank, CultureInfo.InvariantCulture)) + (15 * long.Parse(Branch, CultureInfo.InvariantCulture))
            + (3 * long.Parse(account, CultureInfo.InvariantCulture))) % 97);
        string bban = string.Create(CultureInfo.InvariantCulture, $"{Bank}{Branch}{account}{ribKey:D2}");
        int remainder = 0;
        foreach (char digit in bban + "152700")
        {
            remainder = ((remainder * 10) + (digit - '0')) % 97;
        }

        return string.Create(CultureInfo.InvariantCulture, $"FR{98 - remainder:D2}{bban}");
    }

    private static uint Hash(string text) => BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>Aucun agrégateur : le rapprochement ne propose que l'import de relevés.</summary>
internal sealed class NoBankAggregator : IBankAggregator
{
    public string Name => "";

    public bool IsAvailable => false;

    public Task<IReadOnlyList<BankInstitution>> ListInstitutionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BankInstitution>>([]);

    public Task<BankConsentStart> StartConsentAsync(BankInstitution institution, Guid connectionId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Aucun agrégateur bancaire n'est configuré.");

    public Task<Result<BankConsentGrant>> CompleteConsentAsync(string providerReference, string? callbackCode, CancellationToken cancellationToken) =>
        Task.FromResult<Result<BankConsentGrant>>(BankConnectionErrors.Unavailable);

    public Task<Result<ParsedBankStatement>> FetchAsync(string providerReference, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ParsedBankStatement>>(BankConnectionErrors.Unavailable);

    public Task RevokeAsync(string providerReference, CancellationToken cancellationToken) => Task.CompletedTask;
}
