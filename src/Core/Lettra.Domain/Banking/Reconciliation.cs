using System.Globalization;
using System.Text;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Banking;

/// <summary>Règle qui a produit une proposition, de la plus sûre à la moins sûre (valeurs identiques à <c>MatchRuleDto</c>).</summary>
public enum MatchRule
{
    /// <summary>Le numéro d'une facture ouverte figure dans le libellé ou la référence.</summary>
    InvoiceNumber = 1,

    /// <summary>Montant égal au reste dû d'une facture, et nom du client dans le libellé ou le nom du payeur.</summary>
    AmountAndName = 2,

    /// <summary>Montant égal au reste dû d'une facture, et IBAN du payeur déjà vu pour ce client.</summary>
    AmountAndIban = 3,

    /// <summary>Montant égal à la somme de plusieurs factures ouvertes du même client, reconnu par son nom ou son IBAN.</summary>
    SumOfInvoices = 4,
}

/// <summary>Une opération du relevé à rapprocher, vue par les règles.</summary>
public sealed record BankLineCandidate(
    Guid Id,
    DateOnly BookingDate,
    decimal Amount,
    string Label,
    string? Reference,
    string? CounterpartyName,
    string? CounterpartyIbanHash);

/// <summary>Une facture ou un acompte validé du dossier, avec ce qu'il en reste dû.</summary>
public sealed record OpenInvoice(
    Guid InvoiceId,
    string Number,
    CustomerId CustomerId,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal Outstanding);

/// <summary>IBAN déjà vu pour un client (empreinte, jamais l'IBAN en clair).</summary>
public sealed record KnownIban(CustomerId CustomerId, string IbanHash);

/// <summary>Une imputation proposée.</summary>
public sealed record ProposedAllocation(Guid InvoiceId, string Number, decimal Amount);

/// <summary>
/// Une proposition de rapprochement : le client, les factures et les montants, l'écart de règlement éventuel, et la raison
/// lisible (<see cref="Evidence"/> : numéro trouvé, nom reconnu…). Rien n'est enregistré sans l'accord de l'utilisateur.
/// </summary>
public sealed record MatchProposal(
    Guid BankLineId,
    MatchRule Rule,
    CustomerId CustomerId,
    string CustomerName,
    IReadOnlyList<ProposedAllocation> Allocations,
    decimal WriteOff,
    string Evidence);

/// <summary>
/// Le rapprochement proposé (vague 6), déterministe : les règles sont essayées dans l'ordre de <see cref="MatchRule"/>,
/// la première qui désigne sans ambiguïté un client et des factures l'emporte. Un débit, ou une opération qu'aucune
/// règle ne reconnaît, n'a pas de proposition : l'utilisateur l'impute lui-même ou l'écarte.
/// </summary>
public static class ReconciliationMatcher
{
    /// <summary>Au-delà, la recherche d'une somme de factures s'arrête aux plus anciennes (2^12 combinaisons au plus).</summary>
    public const int MaxInvoicesForSum = 12;

    private static readonly HashSet<string> LegalForms = new(StringComparer.Ordinal)
    {
        "SARL", "SAS", "SASU", "SA", "EURL", "SCI", "SNC", "SCOP", "SCP", "SELARL", "SELAS", "EI", "EIRL", "ETS", "STE", "SOCIETE",
        "CIE", "ET", "LES", "DES", "DE", "DU", "LA", "LE", "SOC", "GROUPE", "ASSOCIATION", "ASSO",
    };

    public static IReadOnlyList<MatchProposal> ProposeAll(
        IEnumerable<BankLineCandidate> lines, IReadOnlyList<OpenInvoice> invoices, IReadOnlyList<KnownIban> knownIbans, decimal writeOffThreshold)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(knownIbans);

        // Une facture proposée pour une opération ne l'est plus pour les suivantes (plus anciennes d'abord).
        Dictionary<Guid, decimal> remaining = invoices.ToDictionary(i => i.InvoiceId, i => i.Outstanding);
        List<MatchProposal> proposals = [];
        foreach (BankLineCandidate line in lines.OrderBy(l => l.BookingDate).ThenBy(l => l.Id))
        {
            List<OpenInvoice> open = [.. invoices.Where(i => remaining[i.InvoiceId] > 0m).Select(i => i with { Outstanding = remaining[i.InvoiceId] })];
            if (Propose(line, open, knownIbans, writeOffThreshold) is { } proposal)
            {
                proposals.Add(proposal);
                foreach (ProposedAllocation allocation in proposal.Allocations)
                {
                    remaining[allocation.InvoiceId] -= allocation.Amount;
                }
            }
        }

        return proposals;
    }

    public static MatchProposal? Propose(BankLineCandidate line, IReadOnlyList<OpenInvoice> invoices, IReadOnlyList<KnownIban> knownIbans, decimal writeOffThreshold)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(knownIbans);
        if (line.Amount <= 0m)
        {
            return null;
        }

        decimal threshold = Math.Max(0m, writeOffThreshold);
        List<OpenInvoice> open = [.. invoices.Where(i => i.Outstanding > 0m).OrderBy(i => i.DueDate ?? i.IssueDate).ThenBy(i => i.Number, StringComparer.Ordinal)];
        List<string> tokens = Tokens($"{line.Label} {line.Reference}");
        List<string> nameTokens = Tokens($"{line.Label} {line.Reference} {line.CounterpartyName}");

        return ByInvoiceNumber(line, open, tokens, threshold)
            ?? ByAmountAndName(line, open, nameTokens, threshold)
            ?? ByAmountAndIban(line, open, knownIbans, threshold)
            ?? BySum(line, open, nameTokens, knownIbans);
    }

    private static MatchProposal? ByInvoiceNumber(BankLineCandidate line, List<OpenInvoice> open, List<string> tokens, decimal threshold)
    {
        List<OpenInvoice> found = [.. open.Where(i => ContainsSequence(tokens, Tokens(i.Number)))];
        if (found.Count == 0 || found.Select(i => i.CustomerId).Distinct().Count() > 1)
        {
            return null;
        }

        List<ProposedAllocation>? allocations = Allocate(line.Amount, found, threshold);
        return allocations is null
            ? null
            : Proposal(line, MatchRule.InvoiceNumber, found[0], allocations, string.Join(", ", found.Where(f => allocations.Any(a => a.InvoiceId == f.InvoiceId)).Select(f => f.Number)));
    }

    private static MatchProposal? ByAmountAndName(BankLineCandidate line, List<OpenInvoice> open, List<string> tokens, decimal threshold)
    {
        List<OpenInvoice> candidates = [.. open.Where(i => Math.Abs(i.Outstanding - line.Amount) <= threshold && NameFound(i.CustomerName, tokens))];
        if (candidates.Count == 0 || candidates.Select(i => i.CustomerId).Distinct().Count() > 1)
        {
            return null;
        }

        // Montants égaux (forfaits mensuels) : la plus ancienne échéance d'abord.
        OpenInvoice invoice = candidates.OrderBy(i => Math.Abs(i.Outstanding - line.Amount)).ThenBy(i => i.DueDate ?? i.IssueDate).First();
        return Proposal(line, MatchRule.AmountAndName, invoice, [new(invoice.InvoiceId, invoice.Number, invoice.Outstanding)], invoice.CustomerName);
    }

    private static MatchProposal? ByAmountAndIban(BankLineCandidate line, List<OpenInvoice> open, IReadOnlyList<KnownIban> knownIbans, decimal threshold)
    {
        if (CustomerByIban(line, knownIbans) is not { } customer)
        {
            return null;
        }

        OpenInvoice? invoice = open
            .Where(i => i.CustomerId == customer && Math.Abs(i.Outstanding - line.Amount) <= threshold)
            .OrderBy(i => Math.Abs(i.Outstanding - line.Amount))
            .ThenBy(i => i.DueDate ?? i.IssueDate)
            .FirstOrDefault();
        return invoice is null
            ? null
            : Proposal(line, MatchRule.AmountAndIban, invoice, [new(invoice.InvoiceId, invoice.Number, invoice.Outstanding)], invoice.CustomerName);
    }

    private static MatchProposal? BySum(BankLineCandidate line, List<OpenInvoice> open, List<string> tokens, IReadOnlyList<KnownIban> knownIbans)
    {
        CustomerId? byIban = CustomerByIban(line, knownIbans);
        List<CustomerId> byName = [.. open.Where(i => NameFound(i.CustomerName, tokens)).Select(i => i.CustomerId).Distinct()];
        CustomerId? customer = byIban ?? (byName.Count == 1 ? byName[0] : null);
        if (customer is null)
        {
            return null;
        }

        List<OpenInvoice> theirs = [.. open.Where(i => i.CustomerId == customer).Take(MaxInvoicesForSum)];
        if (SmallestExactSubset(theirs, line.Amount) is not { Count: >= 2 } subset)
        {
            return null;
        }

        return Proposal(line, MatchRule.SumOfInvoices, subset[0], [.. subset.Select(i => new ProposedAllocation(i.InvoiceId, i.Number, i.Outstanding))], string.Join(", ", subset.Select(i => i.Number)));
    }

    /// <summary>
    /// Impute le montant sur les factures dans l'ordre des échéances. Un reliquat sous le seuil devient un écart de
    /// règlement ; au-delà, un trop-perçu ne se propose pas (il resterait non imputé), un manque laisse la dernière
    /// facture partiellement due.
    /// </summary>
    private static List<ProposedAllocation>? Allocate(decimal amount, List<OpenInvoice> invoices, decimal threshold)
    {
        decimal total = invoices.Sum(i => i.Outstanding);
        if (amount > total + threshold)
        {
            return null;
        }

        if (amount >= total - threshold)
        {
            return [.. invoices.Select(i => new ProposedAllocation(i.InvoiceId, i.Number, i.Outstanding))];
        }

        List<ProposedAllocation> allocations = [];
        decimal left = amount;
        foreach (OpenInvoice invoice in invoices)
        {
            if (left <= 0m)
            {
                break;
            }

            decimal part = Math.Min(left, invoice.Outstanding);
            allocations.Add(new(invoice.InvoiceId, invoice.Number, part));
            left -= part;
        }

        return allocations;
    }

    private static MatchProposal Proposal(BankLineCandidate line, MatchRule rule, OpenInvoice first, List<ProposedAllocation> allocations, string evidence) =>
        new(line.Id, rule, first.CustomerId, first.CustomerName, allocations, allocations.Sum(a => a.Amount) - line.Amount, evidence);

    private static CustomerId? CustomerByIban(BankLineCandidate line, IReadOnlyList<KnownIban> knownIbans)
    {
        if (line.CounterpartyIbanHash is null)
        {
            return null;
        }

        List<CustomerId> customers = [.. knownIbans.Where(k => string.Equals(k.IbanHash, line.CounterpartyIbanHash, StringComparison.Ordinal)).Select(k => k.CustomerId).Distinct()];
        return customers.Count == 1 ? customers[0] : null;
    }

    /// <summary>La plus petite combinaison de factures dont la somme vaut exactement le montant ; les plus anciennes d'abord.</summary>
    private static List<OpenInvoice>? SmallestExactSubset(List<OpenInvoice> invoices, decimal amount)
    {
        int n = invoices.Count;
        List<OpenInvoice>? best = null;
        for (int mask = 1; mask < 1 << n; mask++)
        {
            int size = int.PopCount(mask);
            if (size < 2 || (best is not null && size >= best.Count))
            {
                continue;
            }

            decimal sum = 0m;
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    sum += invoices[i].Outstanding;
                }
            }

            if (sum == amount)
            {
                best = [.. Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => invoices[i])];
            }
        }

        return best;
    }

    /// <summary>Les mots significatifs du nom du client (hors formes juridiques et mots courts) figurent tous dans le texte.</summary>
    private static bool NameFound(string customerName, List<string> tokens)
    {
        List<string> significant = [.. Tokens(customerName).Where(t => t.Length >= 3 && !LegalForms.Contains(t))];
        return significant.Count > 0 && significant.All(tokens.Contains);
    }

    /// <summary>
    /// Vrai si la suite de mots <paramref name="needle"/>, recollés, correspond à des mots consécutifs du texte, eux
    /// aussi recollés : « FAC-2026-000142 » se trouve dans « VIR FAC 2026 000142 » comme dans « FAC2026000142 », mais
    /// pas dans « FAC-2026-0001420 ».
    /// </summary>
    private static bool ContainsSequence(List<string> tokens, List<string> needle)
    {
        string target = string.Concat(needle);
        if (target.Length == 0)
        {
            return false;
        }

        for (int start = 0; start < tokens.Count; start++)
        {
            StringBuilder joined = new();
            for (int end = start; end < tokens.Count && joined.Length < target.Length; end++)
            {
                joined.Append(tokens[end]);
                if (joined.Length == target.Length && string.Equals(joined.ToString(), target, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Mots en majuscules sans accents ; tout ce qui n'est ni lettre ni chiffre sépare.</summary>
    internal static List<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder folded = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            folded.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return [.. folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }
}

/// <summary>
/// Le lettrage automatique : un groupe d'écritures du compte d'un client se lettre quand toutes ses factures sont
/// soldées. Le groupe part d'un encaissement et s'étend aux factures qu'il règle, à leurs avoirs et aux autres
/// encaissements imputés sur ces factures, jusqu'à ce qu'il ne grandisse plus.
/// </summary>
public static class LetteringPlanner
{
    /// <summary>Une pièce de vente du client : son reste dû, et la facture d'origine pour un avoir.</summary>
    public sealed record LedgerInvoice(Guid InvoiceId, Guid? CreditedInvoiceId, DateOnly Date, decimal Outstanding, bool IsCreditNote);

    /// <summary>Un encaissement actif du client et les factures qu'il règle.</summary>
    public sealed record LedgerPayment(Guid PaymentId, DateOnly Date, IReadOnlyCollection<Guid> InvoiceIds);

    /// <summary>
    /// Les écritures à lettrer ensemble à partir de <paramref name="paymentId"/>, ou <c>null</c> si une facture du groupe
    /// reste due (ou a reçu plus que son montant) ou si une écriture du groupe est déjà lettrée.
    /// </summary>
    public static (IReadOnlyList<LetteringItem> Items, DateOnly Date)? Plan(
        Guid paymentId, IReadOnlyList<LedgerInvoice> invoices, IReadOnlyList<LedgerPayment> payments, IReadOnlySet<LetteringItem> alreadyLettered)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(alreadyLettered);
        LedgerPayment? start = payments.FirstOrDefault(p => p.PaymentId == paymentId);
        if (start is null)
        {
            return null;
        }

        Dictionary<Guid, LedgerInvoice> byId = invoices.ToDictionary(i => i.InvoiceId);
        HashSet<Guid> invoiceIds = [.. start.InvoiceIds];
        HashSet<Guid> paymentIds = [start.PaymentId];
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (LedgerPayment payment in payments.Where(p => !paymentIds.Contains(p.PaymentId) && p.InvoiceIds.Any(invoiceIds.Contains)))
            {
                paymentIds.Add(payment.PaymentId);
                invoiceIds.UnionWith(payment.InvoiceIds);
                grew = true;
            }

            foreach (LedgerInvoice credit in invoices.Where(i => i.IsCreditNote && i.CreditedInvoiceId is { } origin && invoiceIds.Contains(origin) && !invoiceIds.Contains(i.InvoiceId)))
            {
                invoiceIds.Add(credit.InvoiceId);
                grew = true;
            }
        }

        if (invoiceIds.Any(id => !byId.TryGetValue(id, out LedgerInvoice? invoice) || (!invoice.IsCreditNote && invoice.Outstanding != 0m)))
        {
            return null;
        }

        List<LetteringItem> items =
        [
            .. invoiceIds.Order().Select(id => new LetteringItem(LetteredEntryKind.Invoice, id)),
            .. paymentIds.Order().Select(id => new LetteringItem(LetteredEntryKind.Payment, id)),
        ];
        if (items.Any(alreadyLettered.Contains))
        {
            return null;
        }

        DateOnly date = invoiceIds.Select(id => byId[id].Date).Concat(payments.Where(p => paymentIds.Contains(p.PaymentId)).Select(p => p.Date)).Max();
        return (items, date);
    }
}
