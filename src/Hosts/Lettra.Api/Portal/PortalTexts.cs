using System.Collections.Frozen;
using System.Globalization;

namespace Lettra.Api.Portal;

/// <summary>
/// Les textes des pages de l'espace client, en français et en anglais : la langue du client (fiche client), sinon celle
/// de son navigateur, sinon le français. Les pages sont servies par le serveur, sans l'application Blazor.
/// </summary>
internal sealed class PortalTexts
{
    private static readonly FrozenDictionary<string, string> French = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Title"] = "Espace client",
        ["Hello"] = "Bonjour {0}",
        ["Intro"] = "Retrouvez ici les factures et les devis que {0} vous a adressés. Vous pouvez payer en ligne et signer vos devis, sans créer de compte.",
        ["ToPay"] = "À payer",
        ["ToPayTotal"] = "Total restant dû : {0}",
        ["NothingToPay"] = "Vous n'avez rien à payer. Merci !",
        ["Invoice"] = "Facture {0}",
        ["DownPayment"] = "Facture d'acompte {0}",
        ["IssuedLabel"] = "Émise le",
        ["DueLabel"] = "À payer avant le",
        ["Overdue"] = "En retard",
        ["Outstanding"] = "Reste à payer",
        ["PayByCard"] = "Payer {0} par carte",
        ["PayByTransfer"] = "Payer par virement",
        ["TransferHelp"] = "Scannez ce code avec l'application de votre banque : le virement s'ouvre déjà rempli (IBAN, montant, référence).",
        ["Iban"] = "IBAN",
        ["Bic"] = "BIC",
        ["Reference"] = "Référence à indiquer",
        ["Quotes"] = "Vos devis",
        ["Quote"] = "Devis {0}",
        ["ValidUntilLabel"] = "Valable jusqu'au",
        ["Expired"] = "Expiré",
        ["ToSign"] = "À signer",
        ["Accepted"] = "Accepté",
        ["Refused"] = "Refusé",
        ["Invoiced"] = "Facturé",
        ["Validated"] = "En attente de réponse",
        ["SeeLines"] = "Voir le détail",
        ["Designation"] = "Désignation",
        ["Quantity"] = "Quantité",
        ["UnitPrice"] = "Prix unitaire HT",
        ["LineTotal"] = "Total HT",
        ["TotalExclTax"] = "Total HT",
        ["TotalVat"] = "TVA",
        ["TotalInclTax"] = "Total TTC",
        ["SignTitle"] = "Signer ce devis en ligne",
        ["SignerName"] = "Vos prénom et nom",
        ["Consent"] = "Je, soussigné(e), accepte ce devis {0} d'un montant de {1} TTC, « bon pour accord ».",
        ["DownPaymentNotice"] = "À la signature, une facture d'acompte de {0} % ({1}) vous sera adressée, payable en ligne.",
        ["Sign"] = "Signer le devis",
        ["SignedBy"] = "Signé en ligne par {0} le {1}.",
        ["SignatureProof"] = "Référence de signature {0} · empreinte du devis {1}",
        ["History"] = "Factures réglées",
        ["Paid"] = "Réglée",
        ["Amount"] = "Montant",
        ["Date"] = "Date",
        ["Number"] = "Numéro",
        ["ValidLink"] = "Ce lien personnel est valable jusqu'au {0}. Ne le transférez pas : il donne accès à vos pièces.",
        ["Questions"] = "Une question ? Répondez simplement au message de {0} qui vous a envoyé ce lien.",
        ["Footer"] = "Espace client proposé par Lettra",
        ["Banner.Paid"] = "Paiement reçu pour la facture {0}. Merci !",
        ["Banner.PaymentPending"] = "Le paiement de la facture {0} n'est pas terminé. Vous pouvez recommencer quand vous voulez.",
        ["Banner.Signed"] = "Devis {0} signé. Merci pour votre confiance !",
        ["Banner.SignedWithDownPayment"] = "Devis {0} signé. La facture d'acompte est prête ci-dessous : vous pouvez la payer en ligne.",
        ["Error.Portal.LinkNotFound"] = "Ce lien n'est plus valable : il a expiré ou a été révoqué. Demandez-en un nouveau à votre fournisseur.",
        ["Error.Portal.Disabled"] = "L'espace client n'est pas ouvert sur ce serveur.",
        ["Error.Portal.QuoteNotSignable"] = "Ce devis ne peut plus être signé en ligne (expiré, ou déjà traité).",
        ["Error.Portal.AlreadySigned"] = "Ce devis est déjà signé.",
        ["Error.Portal.SignerNameInvalid"] = "Indiquez vos prénom et nom (2 à 120 caractères).",
        ["Error.Portal.ConsentRequired"] = "Cochez la case « bon pour accord » pour signer.",
        ["Error.Portal.NothingToPay"] = "Cette facture est déjà réglée.",
        ["Error.Portal.PaymentDeclined"] = "Le paiement a été refusé par votre banque. Aucun montant n'a été débité.",
        ["Error.Portal.CheckoutNotFound"] = "Ce paiement a expiré. Recommencez depuis votre espace client.",
        ["Error.Unexpected"] = "Un problème est survenu. Réessayez dans quelques minutes.",
        ["NotFound.Title"] = "Lien non valable",
        ["Checkout.Title"] = "Paiement sécurisé",
        ["Checkout.Simulated"] = "Démonstration : prestataire de paiement simulé, aucune carte n'est débitée. La carte 4000 0000 0000 0002 est refusée, toute autre est acceptée.",
        ["Checkout.PayTo"] = "Paiement à {0}",
        ["Checkout.For"] = "Facture {0}",
        ["Checkout.Card"] = "Numéro de carte",
        ["Checkout.Expiry"] = "Expiration (MM/AA)",
        ["Checkout.Cvc"] = "Cryptogramme",
        ["Checkout.Pay"] = "Payer {0}",
        ["Checkout.Cancel"] = "Annuler et revenir",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Title"] = "Customer area",
        ["Hello"] = "Hello {0}",
        ["Intro"] = "Here are the invoices and quotes {0} sent you. You can pay online and sign your quotes, without creating an account.",
        ["ToPay"] = "To pay",
        ["ToPayTotal"] = "Total outstanding: {0}",
        ["NothingToPay"] = "You have nothing to pay. Thank you!",
        ["Invoice"] = "Invoice {0}",
        ["DownPayment"] = "Down payment invoice {0}",
        ["IssuedLabel"] = "Issued on",
        ["DueLabel"] = "Due by",
        ["Overdue"] = "Overdue",
        ["Outstanding"] = "Outstanding",
        ["PayByCard"] = "Pay {0} by card",
        ["PayByTransfer"] = "Pay by bank transfer",
        ["TransferHelp"] = "Scan this code with your banking app: the transfer opens already filled in (IBAN, amount, reference).",
        ["Iban"] = "IBAN",
        ["Bic"] = "BIC",
        ["Reference"] = "Reference to quote",
        ["Quotes"] = "Your quotes",
        ["Quote"] = "Quote {0}",
        ["ValidUntilLabel"] = "Valid until",
        ["Expired"] = "Expired",
        ["ToSign"] = "To sign",
        ["Accepted"] = "Accepted",
        ["Refused"] = "Declined",
        ["Invoiced"] = "Invoiced",
        ["Validated"] = "Awaiting your answer",
        ["SeeLines"] = "See the details",
        ["Designation"] = "Description",
        ["Quantity"] = "Quantity",
        ["UnitPrice"] = "Unit price excl. VAT",
        ["LineTotal"] = "Total excl. VAT",
        ["TotalExclTax"] = "Total excl. VAT",
        ["TotalVat"] = "VAT",
        ["TotalInclTax"] = "Total incl. VAT",
        ["SignTitle"] = "Sign this quote online",
        ["SignerName"] = "Your first and last name",
        ["Consent"] = "I, the undersigned, accept quote {0} for {1} incl. VAT.",
        ["DownPaymentNotice"] = "Once signed, you will receive a {0} % down payment invoice ({1}), payable online.",
        ["Sign"] = "Sign the quote",
        ["SignedBy"] = "Signed online by {0} on {1}.",
        ["SignatureProof"] = "Signature reference {0} · quote fingerprint {1}",
        ["History"] = "Paid invoices",
        ["Paid"] = "Paid",
        ["Amount"] = "Amount",
        ["Date"] = "Date",
        ["Number"] = "Number",
        ["ValidLink"] = "This personal link is valid until {0}. Do not forward it: it opens your documents.",
        ["Questions"] = "Any question? Simply reply to the message from {0} that sent you this link.",
        ["Footer"] = "Customer area provided by Lettra",
        ["Banner.Paid"] = "Payment received for invoice {0}. Thank you!",
        ["Banner.PaymentPending"] = "The payment of invoice {0} is not complete. You can try again whenever you like.",
        ["Banner.Signed"] = "Quote {0} signed. Thank you for your trust!",
        ["Banner.SignedWithDownPayment"] = "Quote {0} signed. The down payment invoice is ready below: you can pay it online.",
        ["Error.Portal.LinkNotFound"] = "This link is no longer valid: it has expired or been revoked. Ask your supplier for a new one.",
        ["Error.Portal.Disabled"] = "The customer area is not open on this server.",
        ["Error.Portal.QuoteNotSignable"] = "This quote can no longer be signed online (expired, or already handled).",
        ["Error.Portal.AlreadySigned"] = "This quote is already signed.",
        ["Error.Portal.SignerNameInvalid"] = "Enter your first and last name (2 to 120 characters).",
        ["Error.Portal.ConsentRequired"] = "Tick the acceptance box to sign.",
        ["Error.Portal.NothingToPay"] = "This invoice is already paid.",
        ["Error.Portal.PaymentDeclined"] = "Your bank declined the payment. Nothing was charged.",
        ["Error.Portal.CheckoutNotFound"] = "This payment has expired. Start again from your customer area.",
        ["Error.Unexpected"] = "Something went wrong. Please try again in a few minutes.",
        ["NotFound.Title"] = "Invalid link",
        ["Checkout.Title"] = "Secure payment",
        ["Checkout.Simulated"] = "Demonstration: simulated payment provider, no card is charged. Card 4000 0000 0000 0002 is declined, any other is accepted.",
        ["Checkout.PayTo"] = "Payment to {0}",
        ["Checkout.For"] = "Invoice {0}",
        ["Checkout.Card"] = "Card number",
        ["Checkout.Expiry"] = "Expiry (MM/YY)",
        ["Checkout.Cvc"] = "Security code",
        ["Checkout.Pay"] = "Pay {0}",
        ["Checkout.Cancel"] = "Cancel and go back",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly FrozenDictionary<string, string> _texts;

    private PortalTexts(CultureInfo culture, FrozenDictionary<string, string> texts)
    {
        Culture = culture;
        _texts = texts;
    }

    public CultureInfo Culture { get; }

    /// <summary><c>fr</c> ou <c>en</c>, pour l'attribut <c>lang</c> de la page.</summary>
    public string Language => Culture.TwoLetterISOLanguageName;

    public static IReadOnlyCollection<string> Keys => French.Keys;

    public static IReadOnlyCollection<string> EnglishKeys => English.Keys;

    public string this[string key] => _texts.TryGetValue(key, out string? text) ? text : French.GetValueOrDefault(key, key);

    public string Format(string key, params object?[] values) => string.Format(Culture, this[key], values);

    /// <summary>La langue de la fiche client (<c>fr-FR</c>, <c>en-US</c>) d'abord, puis celle du navigateur.</summary>
    public static PortalTexts For(string? preferred, string? acceptLanguage)
    {
        string? language = preferred ?? acceptLanguage?.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();
        return language is not null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? new PortalTexts(CultureInfo.GetCultureInfo("en-US"), English)
            : new PortalTexts(CultureInfo.GetCultureInfo("fr-FR"), French);
    }

    public string Money(decimal amount) => amount.ToString("C", Culture.Name == "en-US" ? EuroIn(Culture) : Culture);

    public string Date(DateOnly date) => date.ToString("d MMMM yyyy", Culture);

    public string Date(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Domain.SharedKernel.LegalCalendar.Paris).ToString(Culture.Name == "en-US" ? "MMMM d, yyyy 'at' h:mm tt" : "d MMMM yyyy 'à' HH:mm", Culture);

    private static NumberFormatInfo EuroIn(CultureInfo culture)
    {
        NumberFormatInfo format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return format;
    }
}
