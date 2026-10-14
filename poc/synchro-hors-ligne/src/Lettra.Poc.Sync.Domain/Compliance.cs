using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Domain;

/// <summary>
/// Règles déterministes minimales, exécutées à l'identique sur le poste et sur le serveur.
/// Le vrai moteur (mentions obligatoires, Factur-X) vit dans Lettra.Calculations ; ici on prouve
/// seulement que le serveur revérifie un fait au lieu de le croire.
/// </summary>
public static class Compliance
{
    private static readonly decimal[] AllowedVatRates = [0m, 0.021m, 0.055m, 0.10m, 0.20m];

    public static IReadOnlyList<string> Check(Invoice invoice) => Check(invoice.CustomerName, invoice.Lines);

    public static IReadOnlyList<string> Check(string customerName, IReadOnlyList<InvoiceLineDto> lines)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(customerName)) errors.Add("Le client est obligatoire.");
        if (lines.Count == 0) errors.Add("Une facture doit avoir au moins une ligne.");
        foreach (var line in lines)
        {
            if (line.Quantity <= 0) errors.Add($"Quantité invalide sur « {line.Label} ».");
            if (!AllowedVatRates.Contains(line.VatRate)) errors.Add($"Taux de TVA inconnu sur « {line.Label} ».");
        }
        return errors;
    }
}

public sealed class ComplianceException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
