using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Insights;

/// <summary>Ce que la fiche entreprise d'un dossier dit de la société : son SIREN et l'IBAN où elle se fait payer.</summary>
public sealed record GroupCompanyIdentity(string? Siren, string? Iban);

/// <summary>Une société du groupe telle que les autres dossiers la cherchent : son SIREN et son IBAN.</summary>
public sealed record GroupCompanyKey(string Siren, string? Iban);

/// <summary>
/// Une facture ou un acompte validé du dossier à une autre société du groupe (désignée par son SIREN), son montant TTC
/// avoirs déduits et ce que le dossier en dit encaissé.
/// </summary>
public sealed record IntercompanySale(Guid InvoiceId, string Number, DateOnly IssueDate, string CustomerSiren, decimal NetInclTax, decimal Paid);

/// <summary>Une fiche fournisseur du dossier qui porte le SIREN d'une autre société du groupe.</summary>
public sealed record IntercompanySupplier(string Siren, string? Iban);

/// <summary>
/// Un virement sorti de la banque du dossier vers l'IBAN d'une autre société du groupe : son montant (positif), et le
/// libellé et la référence de l'opération, où chercher un numéro de facture.
/// </summary>
public sealed record IntercompanyDebit(string PayeeSiren, DateOnly Date, decimal Amount, string Text);

/// <summary>
/// Ce qu'un dossier sait des autres sociétés du groupe depuis une date. <c>HasBankLines</c> : le dossier a au moins une
/// opération bancaire sur la période, et des IBAN à reconnaître ; ses virements sont alors comparables.
/// </summary>
public sealed record IntercompanyFacts(
    IReadOnlyList<IntercompanySale> Sales,
    IReadOnlyList<IntercompanySupplier> Suppliers,
    IReadOnlyList<IntercompanyDebit> Debits,
    bool HasBankLines);

/// <summary>
/// Lit les dossiers pour les comptes intra-groupe : chaque lecture ouvre la base d'un dossier dans sa propre portée.
/// L'appelant a déjà vérifié que l'utilisateur peut lire ce dossier. Rien ne quitte le serveur : la réponse ne porte
/// ni SIREN ni IBAN. <c>null</c> quand le dossier est introuvable ou illisible.
/// </summary>
public interface IIntercompanyReader
{
    Task<GroupCompanyIdentity?> ReadCompanyAsync(DossierId dossier, CancellationToken cancellationToken);

    /// <summary>Les ventes du dossier aux <paramref name="others"/>, ses fiches fournisseurs à leur SIREN et ses virements vers leur IBAN.</summary>
    Task<IntercompanyFacts?> ReadFactsAsync(DossierId dossier, IReadOnlyList<GroupCompanyKey> others, DateOnly since, CancellationToken cancellationToken);
}
