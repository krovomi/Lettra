using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Pricing;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Components.Billing;
using Lettra.UI.Shared.Components.Feedback;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Lettra.UI.Shared.Billing;

/// <summary>Ce que les éditeurs de devis et de facture font de la même façon : langue du PDF, prix, curseur.</summary>
public static class DocumentEditing
{
    /// <summary>Langue du PDF quand le client n'en a pas.</summary>
    public const string DefaultPdfCulture = "fr-FR";

    /// <summary>
    /// Langue du PDF proposée : celle du client, le français sinon (elle reste modifiable). Null sans client ou sans
    /// annuaire : la langue choisie ne change pas.
    /// </summary>
    public static async Task<string?> PdfCultureOfAsync(Optional<ICustomersGateway> customers, Guid customerId)
    {
        ArgumentNullException.ThrowIfNull(customers);
        if (customerId == Guid.Empty || customers.Value is not { } gateway)
        {
            return null;
        }

        OperationResult<CustomerResponse> customer = await gateway.GetAsync(customerId, CancellationToken.None);
        return customer.Value?.PreferredLanguage ?? DefaultPdfCulture;
    }

    /// <summary>Prix de l'article pour le client de la pièce (grille tarifaire), null sans client ou sans grille.</summary>
    public static async Task<ResolvedPrice?> ResolvePriceAsync(Optional<IPriceListsGateway> prices, Guid customerId, Guid catalogItemId)
    {
        ArgumentNullException.ThrowIfNull(prices);
        if (customerId == Guid.Empty || prices.Value is not { } gateway)
        {
            return null;
        }

        OperationResult<IReadOnlyList<ResolvedPrice>> result = await gateway.ResolveAsync(customerId, [catalogItemId], CancellationToken.None);
        return result.Value is [var first, ..] ? first : null;
    }

    /// <summary>Articles déjà sur la pièce : les suggestions du client ne les reproposent pas.</summary>
    public static Guid[] PresentItems(IEnumerable<DocumentLineRow> lines) =>
        [.. lines.Where(l => l.CatalogItemId is not null).Select(l => l.CatalogItemId!.Value)];

    /// <summary>
    /// Place le curseur sur le champ d'une mention manquante : client, réglages du cabinet, ligne fautive.
    /// <paramref name="own"/> traite d'abord les champs propres à la pièce (échéance, livraison) et dit s'il l'a fait.
    /// </summary>
    public static async Task FocusFindingAsync(ComplianceFindingResponse finding, FindingTargets targets, Func<string, Task<bool>> own)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(own);
        switch (finding.Code)
        {
            case var code when code.StartsWith("Mention.Customer", StringComparison.Ordinal):
                await ExpandAsync(targets.Main);
                if (targets.Customer is not null)
                {
                    await targets.Customer.FocusAsync();
                }

                break;
            case var code when code.StartsWith("Mention.Seller", StringComparison.Ordinal)
                || code is ErrorCodes.Mention.LatePenalties or ErrorCodes.Mention.RecoveryIndemnity
                    or ErrorCodes.Mention.EarlyPaymentDiscount or ErrorCodes.Mention.VatOnDebits:
                targets.Navigation.NavigateTo("settings/company");
                break;
            case var code when await own(code):
                break;
            default:
                await ExpandAsync(targets.Lines);
                if (targets.Grid is not null)
                {
                    await targets.Grid.FocusLineAsync((finding.Line ?? 1) - 1);
                }

                break;
        }
    }

    public static Task ExpandAsync(Collapsible? block) => block?.ExpandAsync() ?? Task.CompletedTask;

    public static async Task FocusAsync(ElementReference element)
    {
        try
        {
            await element.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // Rendu sans navigateur (tests).
        }
    }
}

/// <summary>Les blocs et champs d'un éditeur de pièce où une mention manquante renvoie.</summary>
public sealed record FindingTargets(
    NavigationManager Navigation, Collapsible? Main, CustomerPicker? Customer, Collapsible? Lines, DocumentLinesGrid? Grid);
