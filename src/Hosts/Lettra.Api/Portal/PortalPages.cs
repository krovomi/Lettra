using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Portal;
using Lettra.Exports.Application.Portal;
using Lettra.Printing.Payments;

namespace Lettra.Api.Portal;

/// <summary>Un message en tête de page après une action du client (paiement, signature) : un code connu, jamais du texte libre.</summary>
internal sealed record PortalBanner(string Key, string? Number, bool IsError);

/// <summary>
/// Les pages HTML de l'espace client, rendues par le serveur : légères (aucun script), lisibles sur un téléphone, avec la
/// feuille de style <c>/espace-client.css</c> (la politique de contenu interdit les styles en ligne). Tout texte venu des
/// données est encodé.
/// </summary>
internal static class PortalPages
{
    public const string StylesheetPath = "/espace-client.css";

    // Les lettres accentuées restent lisibles ; seuls les caractères spéciaux du HTML sont encodés.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Portal(PortalView view, PortalTexts t, string basePath, PortalBanner? banner)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(t);
        StringBuilder page = Start(t, $"{t["Title"]} · {view.CompanyName}");
        page.Append("<header class=\"ec-head\"><p class=\"ec-from\">").Append(E(view.CompanyName));
        if (!string.IsNullOrWhiteSpace(view.CompanyCity))
        {
            page.Append(" · ").Append(E(view.CompanyCity));
        }

        page.Append("</p><h1>").Append(E(t.Format("Hello", view.CustomerName))).Append("</h1><p class=\"ec-intro\">")
            .Append(E(t.Format("Intro", view.CompanyName))).Append("</p></header>");
        AppendBanner(page, t, banner);

        List<PortalInvoice> open = [.. view.Invoices.Where(i => i.Outstanding > 0m)];
        List<PortalInvoice> paid = [.. view.Invoices.Where(i => i.Outstanding <= 0m)];
        List<PortalQuote> quotes = [.. view.Quotes];

        // Un lien de signature commence par son devis ; l'espace complet par ce qui reste à payer.
        if (view.FocusQuoteId is not null)
        {
            AppendQuotes(page, view, quotes, t, basePath);
            AppendOpenInvoices(page, view, open, t, basePath);
        }
        else
        {
            AppendOpenInvoices(page, view, open, t, basePath);
            AppendQuotes(page, view, quotes, t, basePath);
        }

        AppendPaid(page, paid, t);
        page.Append("<footer class=\"ec-foot\"><p>").Append(E(t.Format("ValidLink", t.Date(view.LinkExpiresAt)))).Append("</p><p>")
            .Append(E(t.Format("Questions", view.CompanyName))).Append("</p><p class=\"ec-brand\">").Append(E(t["Footer"])).Append("</p></footer>");
        return End(page);
    }

    public static string Message(PortalTexts t, string titleKey, string messageKey)
    {
        ArgumentNullException.ThrowIfNull(t);
        StringBuilder page = Start(t, t[titleKey]);
        page.Append("<header class=\"ec-head\"><h1>").Append(E(t[titleKey])).Append("</h1></header><p class=\"ec-banner is-error\" role=\"alert\">")
            .Append(E(t[messageKey])).Append("</p>");
        return End(page);
    }

    /// <summary>La page du prestataire de paiement simulé : montant, bénéficiaire, carte de test pré-remplie.</summary>
    public static string SimulatedCheckout(Checkout checkout, PortalTexts t, bool declined)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(t);
        StringBuilder page = Start(t, t["Checkout.Title"], "ec-checkout");
        string amount = t.Money(checkout.Request.Amount);
        string action = "/paiement-simule/" + Uri.EscapeDataString(checkout.Id);
        page.Append("<header class=\"ec-head\"><p class=\"ec-from\">").Append(E(t["Checkout.Title"])).Append("</p><h1>").Append(E(amount))
            .Append("</h1><p>").Append(E(t.Format("Checkout.PayTo", checkout.Request.PayeeName))).Append(" · ")
            .Append(E(t.Format("Checkout.For", checkout.Request.InvoiceNumber))).Append("</p></header>");
        if (declined)
        {
            page.Append("<p class=\"ec-banner is-error\" role=\"alert\" id=\"checkout-declined\">").Append(E(t["Error.Portal.PaymentDeclined"])).Append("</p>");
        }

        page.Append("<p class=\"ec-note\">").Append(E(t["Checkout.Simulated"])).Append("</p>")
            .Append("<form method=\"post\" action=\"").Append(E(action)).Append("\" class=\"ec-card ec-form\" id=\"checkout-form\">")
            .Append(Field("card", t["Checkout.Card"], "4242 4242 4242 4242", "cc-number", "numeric"))
            .Append("<div class=\"ec-row\">")
            .Append(Field("expiry", t["Checkout.Expiry"], "12/34", "cc-exp", "numeric"))
            .Append(Field("cvc", t["Checkout.Cvc"], "123", "cc-csc", "numeric"))
            .Append("</div><button type=\"submit\" name=\"decision\" value=\"pay\" class=\"ec-button\" id=\"checkout-pay\">")
            .Append(E(t.Format("Checkout.Pay", amount))).Append("</button>")
            .Append("<button type=\"submit\" name=\"decision\" value=\"cancel\" class=\"ec-link\" id=\"checkout-cancel\" formnovalidate>")
            .Append(E(t["Checkout.Cancel"])).Append("</button></form>");
        return End(page);
    }

    private static void AppendBanner(StringBuilder page, PortalTexts t, PortalBanner? banner)
    {
        if (banner is null)
        {
            return;
        }

        page.Append("<p id=\"portal-banner\" class=\"ec-banner ").Append(banner.IsError ? "is-error" : "is-success").Append("\" role=\"")
            .Append(banner.IsError ? "alert" : "status").Append("\">").Append(E(t.Format(banner.Key, banner.Number))).Append("</p>");
    }

    private static void AppendOpenInvoices(StringBuilder page, PortalView view, List<PortalInvoice> open, PortalTexts t, string basePath)
    {
        page.Append("<section id=\"portal-invoices\" class=\"ec-section\" aria-labelledby=\"portal-invoices-title\"><h2 id=\"portal-invoices-title\">")
            .Append(E(t["ToPay"])).Append("</h2>");
        if (open.Count == 0)
        {
            page.Append("<p class=\"ec-empty\">").Append(E(t["NothingToPay"])).Append("</p></section>");
            return;
        }

        page.Append("<p class=\"ec-total\" id=\"portal-total\">").Append(E(t.Format("ToPayTotal", t.Money(open.Sum(i => i.Outstanding))))).Append("</p>");
        foreach (PortalInvoice invoice in open)
        {
            string title = t.Format(invoice.Kind == InvoiceKindDto.DownPayment ? "DownPayment" : "Invoice", invoice.Number);
            page.Append("<article class=\"ec-card\" id=\"invoice-").Append(invoice.Id).Append("\" data-number=\"").Append(E(invoice.Number)).Append("\">")
                .Append("<div class=\"ec-card-head\"><h3>").Append(E(title)).Append("</h3>");
            if (invoice.IsOverdue)
            {
                page.Append("<span class=\"ec-badge is-late\">").Append(E(t["Overdue"])).Append("</span>");
            }

            page.Append("</div><dl class=\"ec-facts\"><div><dt>").Append(E(t["IssuedLabel"])).Append("</dt><dd>").Append(E(t.Date(invoice.IssueDate))).Append("</dd></div>");
            if (invoice.DueDate is DateOnly due)
            {
                page.Append("<div><dt>").Append(E(t["DueLabel"])).Append("</dt><dd>").Append(E(t.Date(due))).Append("</dd></div>");
            }

            page.Append("<div><dt>").Append(E(t["Outstanding"])).Append("</dt><dd class=\"ec-amount\">").Append(E(t.Money(invoice.Outstanding))).Append("</dd></div></dl>");
            if (view.CanPayOnline)
            {
                page.Append("<form method=\"post\" action=\"").Append(E($"{basePath}/factures/{invoice.Id}/payer")).Append("\">")
                    .Append("<button type=\"submit\" class=\"ec-button\" id=\"pay-").Append(invoice.Id).Append("\">")
                    .Append(E(t.Format("PayByCard", t.Money(invoice.Outstanding)))).Append("</button></form>");
            }

            AppendTransfer(page, view, invoice, t);
            page.Append("</article>");
        }

        page.Append("</section>");
    }

    private static void AppendTransfer(StringBuilder page, PortalView view, PortalInvoice invoice, PortalTexts t)
    {
        string? payload = EpcPaymentCode.Payload(view.CompanyName, view.Iban, view.Bic, invoice.Outstanding, invoice.Number);
        if (payload is null)
        {
            return;
        }

        QrCode code = QrCode.Encode(payload);
        int side = QrImages.ViewBoxSize(code);
        page.Append("<details class=\"ec-transfer\"><summary>").Append(E(t["PayByTransfer"])).Append("</summary><div class=\"ec-transfer-body\">")
            .Append("<svg viewBox=\"0 0 ").Append(side).Append(' ').Append(side).Append("\" role=\"img\" aria-label=\"").Append(E(t["PayByTransfer"]))
            .Append("\" shape-rendering=\"crispEdges\" class=\"ec-qr\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"")
            .Append(QrImages.SvgPath(code)).Append("\" fill=\"#000\"/></svg><div><p>").Append(E(t["TransferHelp"])).Append("</p><dl class=\"ec-facts\">")
            .Append("<div><dt>").Append(E(t["Iban"])).Append("</dt><dd class=\"ec-mono\">").Append(E(Grouped(EpcPaymentCode.NormalizedIban(view.Iban) ?? ""))).Append("</dd></div>");
        if (!string.IsNullOrWhiteSpace(view.Bic))
        {
            page.Append("<div><dt>").Append(E(t["Bic"])).Append("</dt><dd class=\"ec-mono\">").Append(E(view.Bic)).Append("</dd></div>");
        }

        page.Append("<div><dt>").Append(E(t["Reference"])).Append("</dt><dd class=\"ec-mono\">").Append(E(invoice.Number)).Append("</dd></div></dl></div></div></details>");
    }

    private static void AppendQuotes(StringBuilder page, PortalView view, List<PortalQuote> quotes, PortalTexts t, string basePath)
    {
        if (quotes.Count == 0)
        {
            return;
        }

        page.Append("<section id=\"portal-quotes\" class=\"ec-section\" aria-labelledby=\"portal-quotes-title\"><h2 id=\"portal-quotes-title\">")
            .Append(E(t["Quotes"])).Append("</h2>");
        foreach (PortalQuote quote in quotes)
        {
            bool focus = quote.Id == view.FocusQuoteId;
            page.Append("<article class=\"ec-card").Append(focus ? " is-focus" : "").Append("\" id=\"quote-").Append(quote.Id).Append("\" data-number=\"")
                .Append(E(quote.Number)).Append("\"><div class=\"ec-card-head\"><h3>").Append(E(t.Format("Quote", quote.Number))).Append("</h3>")
                .Append("<span class=\"ec-badge ").Append(BadgeClass(quote)).Append("\">").Append(E(t[StatusKey(quote)])).Append("</span></div>");
            if (!string.IsNullOrWhiteSpace(quote.Subject))
            {
                page.Append("<p class=\"ec-subject\">").Append(E(quote.Subject)).Append("</p>");
            }

            page.Append("<dl class=\"ec-facts\"><div><dt>").Append(E(t["IssuedLabel"])).Append("</dt><dd>").Append(E(t.Date(quote.IssueDate))).Append("</dd></div>");
            if (quote.ValidUntil is DateOnly until)
            {
                page.Append("<div><dt>").Append(E(t["ValidUntilLabel"])).Append("</dt><dd>").Append(E(t.Date(until))).Append("</dd></div>");
            }

            page.Append("<div><dt>").Append(E(t["TotalInclTax"])).Append("</dt><dd class=\"ec-amount\">").Append(E(t.Money(quote.TotalInclTax))).Append("</dd></div></dl>");
            AppendLines(page, quote, t, open: focus && quote.CanSign);
            if (quote.Signature is { } signature)
            {
                page.Append("<p class=\"ec-signed\" id=\"signed-").Append(quote.Id).Append("\">").Append(E(t.Format("SignedBy", signature.SignerName, t.Date(signature.SignedAt))))
                    .Append("<br><small>").Append(E(t.Format("SignatureProof", signature.ProviderReference, signature.DocumentHash[..Math.Min(16, signature.DocumentHash.Length)])))
                    .Append("</small></p>");
            }
            else if (quote.CanSign)
            {
                AppendSignForm(page, view, quote, t, basePath);
            }

            page.Append("</article>");
        }

        page.Append("</section>");
    }

    private static void AppendLines(StringBuilder page, PortalQuote quote, PortalTexts t, bool open)
    {
        page.Append("<details class=\"ec-lines\"").Append(open ? " open" : "").Append("><summary>").Append(E(t["SeeLines"]))
            .Append("</summary><div class=\"ec-table-wrap\"><table><thead><tr><th scope=\"col\">").Append(E(t["Designation"])).Append("</th><th scope=\"col\" class=\"num\">")
            .Append(E(t["Quantity"])).Append("</th><th scope=\"col\" class=\"num\">").Append(E(t["UnitPrice"])).Append("</th><th scope=\"col\" class=\"num\">")
            .Append(E(t["LineTotal"])).Append("</th></tr></thead><tbody>");
        foreach (PortalQuoteLine line in quote.Lines)
        {
            page.Append("<tr><td>").Append(E(line.Designation)).Append("</td><td class=\"num\">").Append(E(line.Quantity.ToString("0.###", t.Culture)))
                .Append("</td><td class=\"num\">").Append(E(t.Money(line.UnitPriceExclTax))).Append("</td><td class=\"num\">").Append(E(t.Money(line.TotalExclTax))).Append("</td></tr>");
        }

        page.Append("</tbody><tfoot><tr><th scope=\"row\" colspan=\"3\">").Append(E(t["TotalExclTax"])).Append("</th><td class=\"num\">").Append(E(t.Money(quote.TotalExclTax)))
            .Append("</td></tr><tr><th scope=\"row\" colspan=\"3\">").Append(E(t["TotalVat"])).Append("</th><td class=\"num\">").Append(E(t.Money(quote.TotalVat)))
            .Append("</td></tr><tr><th scope=\"row\" colspan=\"3\">").Append(E(t["TotalInclTax"])).Append("</th><td class=\"num\">").Append(E(t.Money(quote.TotalInclTax)))
            .Append("</td></tr></tfoot></table></div></details>");
    }

    private static void AppendSignForm(StringBuilder page, PortalView view, PortalQuote quote, PortalTexts t, string basePath)
    {
        string id = quote.Id.ToString("N", CultureInfo.InvariantCulture);
        page.Append("<form method=\"post\" action=\"").Append(E($"{basePath}/devis/{quote.Id}/signer")).Append("\" class=\"ec-form ec-sign\" id=\"sign-")
            .Append(quote.Id).Append("\"><h4>").Append(E(t["SignTitle"])).Append("</h4>")
            .Append(Field("signer", t["SignerName"], "", "name", "text", id))
            .Append("<label class=\"ec-check\"><input type=\"checkbox\" name=\"consent\" value=\"true\" required id=\"consent-").Append(id).Append("\"> <span>")
            .Append(E(t.Format("Consent", quote.Number, t.Money(quote.TotalInclTax)))).Append("</span></label>");
        if (view.DownPaymentPercent is decimal percent && quote.Id == view.FocusQuoteId)
        {
            decimal amount = Math.Round(quote.TotalInclTax * percent / 100m, 2, MidpointRounding.AwayFromZero);
            page.Append("<p class=\"ec-note\" id=\"down-payment-notice\">").Append(E(t.Format("DownPaymentNotice", percent.ToString("0.##", t.Culture), t.Money(amount)))).Append("</p>");
        }

        page.Append("<button type=\"submit\" class=\"ec-button\" id=\"sign-button-").Append(quote.Id).Append("\">").Append(E(t["Sign"])).Append("</button></form>");
    }

    private static void AppendPaid(StringBuilder page, List<PortalInvoice> paid, PortalTexts t)
    {
        if (paid.Count == 0)
        {
            return;
        }

        page.Append("<section id=\"portal-history\" class=\"ec-section\" aria-labelledby=\"portal-history-title\"><h2 id=\"portal-history-title\">").Append(E(t["History"]))
            .Append("</h2><div class=\"ec-table-wrap\"><table><thead><tr><th scope=\"col\">").Append(E(t["Number"])).Append("</th><th scope=\"col\">").Append(E(t["Date"]))
            .Append("</th><th scope=\"col\" class=\"num\">").Append(E(t["Amount"])).Append("</th></tr></thead><tbody>");
        foreach (PortalInvoice invoice in paid)
        {
            page.Append("<tr><td>").Append(E(invoice.Number)).Append("</td><td>").Append(E(t.Date(invoice.IssueDate))).Append("</td><td class=\"num\">")
                .Append(E(t.Money(invoice.TotalInclTax))).Append(" <span class=\"ec-badge is-done\">").Append(E(t["Paid"])).Append("</span></td></tr>");
        }

        page.Append("</tbody></table></div></section>");
    }

    private static string StatusKey(PortalQuote quote) => quote switch
    {
        { CanSign: true } => "ToSign",
        { Status: QuoteStatusDto.Accepted } => "Accepted",
        { Status: QuoteStatusDto.Refused } => "Refused",
        { Status: QuoteStatusDto.Invoiced } => "Invoiced",
        { Status: QuoteStatusDto.Validated, IsExpired: true } => "Expired",
        _ => "Validated",
    };

    private static string BadgeClass(PortalQuote quote) => StatusKey(quote) switch
    {
        "ToSign" => "is-todo",
        "Accepted" or "Invoiced" => "is-done",
        _ => "is-muted",
    };

    private static string Field(string name, string label, string value, string autocomplete, string mode, string? suffix = null)
    {
        string id = suffix is null ? name : $"{name}-{suffix}";
        return $"<label class=\"ec-field\" for=\"{id}\"><span>{E(label)}</span><input id=\"{id}\" name=\"{name}\" value=\"{E(value)}\" autocomplete=\"{autocomplete}\" " +
            $"inputmode=\"{mode}\" required maxlength=\"120\"></label>";
    }

    private static string Grouped(string iban) => string.Join(' ', iban.Chunk(4).Select(c => new string(c)));

    private static StringBuilder Start(PortalTexts t, string title, string bodyClass = "ec-portal")
    {
        StringBuilder page = new(16_384);
        page.Append("<!doctype html><html lang=\"").Append(t.Language).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"robots\" content=\"noindex, nofollow\">")
            .Append("<meta name=\"referrer\" content=\"same-origin\"><title>").Append(E(title)).Append("</title>")
            .Append("<link rel=\"stylesheet\" href=\"").Append(StylesheetPath).Append("\"></head><body class=\"").Append(bodyClass).Append("\"><main class=\"ec\">");
        return page;
    }

    private static string End(StringBuilder page) => page.Append("</main></body></html>").ToString();

    private static string E(string? text) => Html.Encode(text ?? "");
}
