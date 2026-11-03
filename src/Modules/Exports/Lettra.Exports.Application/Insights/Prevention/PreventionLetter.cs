using System.Text;
using Lettra.Contracts.Insights;
using Lettra.Domain.Parties;

namespace Lettra.Exports.Application.Insights.Prevention;

/// <summary>
/// Le courrier type « proposition de rendez-vous de prévention », en français et en anglais, sans IA. Il propose un
/// rendez-vous et cite les sujets à aborder en termes neutres : jamais le niveau d'attention, jamais le mot
/// « difficulté », aucun chiffre. Le collaborateur le relit et le modifie avant l'envoi. Texte à faire relire par un
/// expert-comptable.
/// </summary>
public static class PreventionLetterTemplates
{
    public const string English = DocumentLanguages.English;

    public const string French = DocumentLanguages.French;

    /// <summary><c>en-US</c> pour un courrier en anglais, <c>fr-FR</c> sinon.</summary>
    public static string LanguageOf(string? preferred) =>
        preferred is not null && preferred.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : French;

    public static (string Subject, string Body) Write(string language, string companyName, IReadOnlyList<PreventionSignalDto> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        bool english = language == English;
        string? company = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        StringBuilder body = new();
        if (english)
        {
            body.Append("Hello,\n\n");
            body.Append("We follow the activity of ").Append(company ?? "your company").Append(" closely and would like to suggest a meeting to review the coming months together.\n\n");
            AppendTopics(body, topics, "In particular, we would like to discuss:", english: true);
            body.Append("The meeting takes about an hour and can be held at our office, at your premises or by video call. ");
            body.Append("Could you let us know two or three time slots that would suit you in the coming days?\n\n");
            body.Append("Kind regards,\n\nYour accountant");
            return ("Meeting proposal: reviewing the coming months together", body.ToString());
        }

        body.Append("Bonjour,\n\n");
        // L'entreprise en apposition : « de votre entreprise, Atelier Durand, » évite l'élision fautive « de Atelier ».
        body.Append("Nous suivons de près l'activité de votre entreprise").Append(company is null ? string.Empty : ", " + company + ",")
            .Append(" et souhaitons vous proposer un rendez-vous pour faire le point ensemble sur les prochains mois.\n\n");
        AppendTopics(body, topics, "Nous aimerions notamment aborder :", english: false);
        body.Append("Ce rendez-vous d'une heure environ peut se tenir à notre cabinet, chez vous ou en visioconférence. ");
        body.Append("Pourriez-vous nous indiquer deux ou trois créneaux qui vous conviendraient dans les prochains jours ?\n\n");
        body.Append("Bien cordialement,\n\nVotre expert-comptable");
        return ("Proposition de rendez-vous : faire le point sur les prochains mois", body.ToString());
    }

    /// <summary>Le sujet d'un signal, dit au client sans jugement.</summary>
    public static string Topic(PreventionSignalDto signal, bool english) => (signal, english) switch
    {
        (PreventionSignalDto.Treasury, false) => "votre trésorerie des prochaines semaines",
        (PreventionSignalDto.Treasury, true) => "your cash position over the coming weeks",
        (PreventionSignalDto.PaymentDelay, false) => "les délais de paiement de vos clients",
        (PreventionSignalDto.PaymentDelay, true) => "how quickly your customers pay",
        (PreventionSignalDto.PeerGap, false) => "l'évolution de votre chiffre d'affaires",
        (PreventionSignalDto.PeerGap, true) => "how your revenue is developing",
        (PreventionSignalDto.VatBeyondCollections, false) => "la prochaine échéance de TVA",
        (PreventionSignalDto.VatBeyondCollections, true) => "your next VAT payment",
        (PreventionSignalDto.TopCustomer, false) => "la répartition de votre chiffre d'affaires entre vos clients",
        (PreventionSignalDto.TopCustomer, true) => "how your revenue is spread across customers",
        (PreventionSignalDto.CustomersInProceedings, false) => "la situation de certains de vos clients",
        (PreventionSignalDto.CustomersInProceedings, true) => "the situation of some of your customers",
        (PreventionSignalDto.BilledCollectedGap, false) => "le suivi de vos encaissements",
        (PreventionSignalDto.BilledCollectedGap, true) => "keeping track of incoming payments",
        _ => english ? "your activity" : "votre activité",
    };

    private static void AppendTopics(StringBuilder body, IReadOnlyList<PreventionSignalDto> topics, string intro, bool english)
    {
        string[] lines = [.. topics.Distinct().Select(t => Topic(t, english))];
        if (lines.Length == 0)
        {
            return;
        }

        body.Append(intro).Append('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string end = english ? (i == lines.Length - 1 ? "." : ";") : (i == lines.Length - 1 ? "." : " ;");
            body.Append("- ").Append(lines[i]).Append(end).Append('\n');
        }

        body.Append('\n');
    }
}
