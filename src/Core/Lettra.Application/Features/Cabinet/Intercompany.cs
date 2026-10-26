using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Cabinet;

/// <summary>Les comptes entre les sociétés d'un même groupe, parmi les dossiers que l'utilisateur peut lire.</summary>
public sealed record GetIntercompanyQuery : IQuery<Result<IntercompanyResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Une société du groupe : son dossier, son nom au cabinet, ce que dit sa fiche entreprise.</summary>
public sealed record GroupCompany(Guid Dossier, string Name, string Siren, string? Iban);

/// <summary>
/// Deux passes sur les dossiers lisibles : les fiches entreprise d'abord (qui est qui), puis, dans chaque dossier d'une
/// société identifiée, ses ventes aux autres, ses fiches fournisseurs et ses virements vers eux. Le rapprochement est
/// fait par <see cref="IntercompanyReconciliation"/>.
/// </summary>
internal sealed class IntercompanyHandler(AccessibleDossiers accessible, IIntercompanyReader reader, TimeProvider clock)
    : IQueryHandler<GetIntercompanyQuery, Result<IntercompanyResponse>>
{
    public async Task<Result<IntercompanyResponse>> HandleAsync(GetIntercompanyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = LegalCalendar.DateOf(clock.GetUtcNow());
        DateOnly since = today.AddMonths(-IntercompanyResponse.Months);
        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(query.Permission, cancellationToken).ConfigureAwait(false);

        List<GroupCompany> companies = [];
        foreach (DossierSummary dossier in dossiers)
        {
            GroupCompanyIdentity? identity = await reader.ReadCompanyAsync(new DossierId(dossier.Id), cancellationToken).ConfigureAwait(false);
            if (identity?.Siren is { } siren && companies.TrueForAll(c => c.Siren != siren))
            {
                companies.Add(new GroupCompany(dossier.Id, dossier.Name, siren, identity.Iban));
            }
        }

        Dictionary<Guid, IntercompanyFacts> facts = [];
        if (companies.Count > 1)
        {
            foreach (GroupCompany company in companies)
            {
                GroupCompanyKey[] others = [.. companies.Where(c => c != company).Select(c => new GroupCompanyKey(c.Siren, c.Iban))];
                IntercompanyFacts? read = await reader.ReadFactsAsync(new DossierId(company.Dossier), others, since, cancellationToken).ConfigureAwait(false);
                if (read is not null)
                {
                    facts[company.Dossier] = read;
                }
            }
        }

        return new IntercompanyResponse(today, since, dossiers.Count, IntercompanyReconciliation.Pairs(companies, facts));
    }
}

/// <summary>
/// Le rapprochement intra-groupe, pur et déterministe. Un couple (vendeur, acheteur) existe dès que le vendeur a facturé
/// l'acheteur sur la période. Chaque facture cherche le virement de l'acheteur qui la paie : d'abord celui dont le
/// libellé porte son numéro, sinon un virement du même montant ; un virement ne paie qu'une facture. Les écarts :
/// fiche fournisseur absente ou à un autre IBAN, facture encaissée sans virement vu, virement vu sans encaissement,
/// totaux qui ne concordent pas. Sans opération bancaire chez l'acheteur, les paiements ne sont pas comparés.
/// </summary>
public static class IntercompanyReconciliation
{
    public static IReadOnlyList<IntercompanyPairResponse> Pairs(IReadOnlyList<GroupCompany> companies, IReadOnlyDictionary<Guid, IntercompanyFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(facts);
        List<IntercompanyPairResponse> pairs = [];
        foreach (GroupCompany seller in companies)
        {
            if (!facts.TryGetValue(seller.Dossier, out IntercompanyFacts? sold))
            {
                continue;
            }

            foreach (GroupCompany buyer in companies.Where(c => c != seller))
            {
                IntercompanySale[] sales = [.. sold.Sales.Where(s => s.CustomerSiren == buyer.Siren).OrderBy(s => s.IssueDate).ThenBy(s => s.Number, StringComparer.Ordinal)];
                if (sales.Length > 0)
                {
                    pairs.Add(Pair(seller, buyer, sales, facts.GetValueOrDefault(buyer.Dossier)));
                }
            }
        }

        return
        [
            .. pairs
                .OrderByDescending(p => p.Gaps.Count > 0)
                .ThenBy(p => p.SellerName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.BuyerName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    private static IntercompanyPairResponse Pair(GroupCompany seller, GroupCompany buyer, IntercompanySale[] sales, IntercompanyFacts? bought)
    {
        bool bankLinked = bought?.HasBankLines == true;
        IntercompanyDebit[] debits = [.. (bought?.Debits ?? []).Where(d => d.PayeeSiren == seller.Siren).OrderBy(d => d.Date)];
        IntercompanyDebit?[] matched = Match(sales, debits);

        IntercompanyInvoiceResponse[] invoices = [.. sales.Select((s, i) => Invoice(s, matched[i] is not null, bankLinked))];
        decimal paidBySeller = sales.Sum(s => s.Paid);
        decimal paidByBuyer = debits.Sum(d => d.Amount);

        List<IntercompanyGapDto> gaps = [];
        IntercompanySupplier? supplier = bought?.Suppliers.FirstOrDefault(s => s.Siren == seller.Siren);
        if (bought is not null && supplier is null)
        {
            gaps.Add(IntercompanyGapDto.SupplierMissing);
        }
        else if (supplier?.Iban is { } known && seller.Iban is { } iban && !SameIban(known, iban))
        {
            gaps.Add(IntercompanyGapDto.SupplierIbanDiffers);
        }

        gaps.AddRange(invoices.Where(i => i.Gap is not null).Select(i => i.Gap!.Value).Distinct().Order());
        if (bankLinked && Math.Abs(paidBySeller - paidByBuyer) >= IntercompanyResponse.Tolerance)
        {
            gaps.Add(IntercompanyGapDto.TotalsDiffer);
        }

        return new IntercompanyPairResponse(
            seller.Dossier,
            seller.Name,
            buyer.Dossier,
            buyer.Name,
            sales.Sum(s => s.NetInclTax),
            paidBySeller,
            paidByBuyer,
            bankLinked,
            gaps,
            invoices);
    }

    /// <summary>Le virement de chaque facture : son numéro dans le libellé d'abord, puis le même montant ; chacun ne sert qu'une fois.</summary>
    private static IntercompanyDebit?[] Match(IntercompanySale[] sales, IntercompanyDebit[] debits)
    {
        IntercompanyDebit?[] matched = new IntercompanyDebit?[sales.Length];
        HashSet<IntercompanyDebit> used = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < sales.Length; i++)
        {
            matched[i] = Take(debits, used, d => d.Text.Contains(sales[i].Number, StringComparison.OrdinalIgnoreCase));
        }

        for (int i = 0; i < sales.Length; i++)
        {
            IntercompanySale sale = sales[i];
            matched[i] ??= Take(debits, used, d => d.Date >= sale.IssueDate && Math.Abs(d.Amount - sale.NetInclTax) < IntercompanyResponse.Tolerance);
        }

        return matched;
    }

    private static IntercompanyDebit? Take(IntercompanyDebit[] debits, HashSet<IntercompanyDebit> used, Func<IntercompanyDebit, bool> fits)
    {
        IntercompanyDebit? debit = debits.FirstOrDefault(d => !used.Contains(d) && fits(d));
        if (debit is not null)
        {
            used.Add(debit);
        }

        return debit;
    }

    private static IntercompanyInvoiceResponse Invoice(IntercompanySale sale, bool seen, bool bankLinked)
    {
        bool paid = sale.NetInclTax >= IntercompanyResponse.Tolerance && sale.Paid >= sale.NetInclTax - IntercompanyResponse.Tolerance;
        IntercompanyGapDto? gap = !bankLinked ? null
            : paid && !seen ? IntercompanyGapDto.PaidNotSeen
            : seen && !paid ? IntercompanyGapDto.SeenNotPaid
            : null;
        return new IntercompanyInvoiceResponse(sale.InvoiceId, sale.Number, sale.IssueDate, sale.NetInclTax, sale.Paid, seen, gap);
    }

    private static bool SameIban(string left, string right) =>
        string.Equals(Compact(left), Compact(right), StringComparison.OrdinalIgnoreCase);

    private static string Compact(string iban) => iban.Replace(" ", string.Empty, StringComparison.Ordinal);
}
