using System.Globalization;
using System.Text;
using Lettra.Domain.Parties;

namespace Lettra.Application.Features.Billing;

/// <summary>Tout ce que le texte d'une relance de devis cite.</summary>
public sealed record QuoteFollowUpLetter(
    string Language,
    string QuoteNumber,
    DateOnly IssueDate,
    DateOnly ValidUntil,
    int DaysLeft,
    string? Subject,
    decimal TotalExclTax,
    decimal TotalInclTax,
    string SenderName);

/// <summary>
/// Le modèle de la relance d'un devis, en français et en anglais (la langue du client ; français par défaut). Un ton
/// cordial : rappeler l'offre et sa fin de validité, proposer d'en parler, dire comment l'accepter. Écrit sans IA ;
/// l'utilisateur le relit et peut le modifier avant l'envoi.
/// </summary>
public static class QuoteFollowUpTemplates
{
    /// <summary><c>en-US</c> pour un client anglophone, <c>fr-FR</c> sinon.</summary>
    public static string LanguageOf(string? preferred) =>
        preferred is not null && preferred.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? DocumentLanguages.English : DocumentLanguages.French;

    public static (string Subject, string Body) Write(QuoteFollowUpLetter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return letter.Language == DocumentLanguages.English ? WriteEnglish(letter) : WriteFrench(letter);
    }

    private static (string Subject, string Body) WriteFrench(QuoteFollowUpLetter l)
    {
        CultureInfo fr = CultureInfo.GetCultureInfo(DocumentLanguages.French);
        string until = l.ValidUntil.ToString("d MMMM yyyy", fr);
        string about = string.IsNullOrWhiteSpace(l.Subject) ? "" : $" (« {l.Subject.Trim()} »)";
        string validity = l.DaysLeft switch
        {
            <= 0 => "Ce devis arrive à échéance aujourd'hui.",
            1 => $"Ce devis reste valable jusqu'à demain, {until}.",
            _ => string.Create(fr, $"Ce devis reste valable jusqu'au {until}, encore {l.DaysLeft} jours."),
        };
        StringBuilder body = new();
        body.Append("Bonjour,\n\n");
        body.Append(fr, $"Nous revenons vers vous au sujet de notre devis n° {l.QuoteNumber} du {l.IssueDate.ToString("d MMMM yyyy", fr)}{about}, ");
        body.Append(fr, $"d'un montant de {Money(l.TotalExclTax, fr)} HT, soit {Money(l.TotalInclTax, fr)} TTC.\n\n");
        body.Append(validity).Append("\n\n");
        body.Append("Avez-vous pu l'étudier ? Nous restons à votre disposition pour en parler, l'adapter à votre besoin ou répondre à vos questions. ");
        body.Append("Pour l'accepter, il suffit de nous le retourner signé ou de répondre à ce message.\n\n");
        body.Append(fr, $"Cordialement,\n{l.SenderName}\n");
        return ($"Votre devis {l.QuoteNumber} : valable jusqu'au {until}", body.ToString());
    }

    private static (string Subject, string Body) WriteEnglish(QuoteFollowUpLetter l)
    {
        CultureInfo en = CultureInfo.GetCultureInfo("en-GB");
        string until = l.ValidUntil.ToString("d MMMM yyyy", en);
        string about = string.IsNullOrWhiteSpace(l.Subject) ? "" : $" (\"{l.Subject.Trim()}\")";
        string validity = l.DaysLeft switch
        {
            <= 0 => "This quote expires today.",
            1 => $"This quote is valid until tomorrow, {until}.",
            _ => string.Create(en, $"This quote is valid until {until}, {l.DaysLeft} more days."),
        };
        StringBuilder body = new();
        body.Append("Hello,\n\n");
        body.Append(en, $"We are following up on our quote {l.QuoteNumber} dated {l.IssueDate.ToString("d MMMM yyyy", en)}{about}, ");
        body.Append(en, $"for {Money(l.TotalExclTax, en)} excluding VAT ({Money(l.TotalInclTax, en)} including VAT).\n\n");
        body.Append(validity).Append("\n\n");
        body.Append("Have you had a chance to review it? We would be happy to discuss it, adjust it to your needs or answer any questions. ");
        body.Append("To accept it, simply return it signed or reply to this message.\n\n");
        body.Append(en, $"Kind regards,\n{l.SenderName}\n");
        return ($"Your quote {l.QuoteNumber}: valid until {until}", body.ToString());
    }

    /// <summary>« 1 234,56 € » en français (espace insécable avant le symbole), « €1,234.56 » en anglais.</summary>
    private static string Money(decimal amount, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr"
            ? string.Create(culture, $"{amount:N2}\u00a0€")
            : string.Create(culture, $"€{amount:N2}");
}
