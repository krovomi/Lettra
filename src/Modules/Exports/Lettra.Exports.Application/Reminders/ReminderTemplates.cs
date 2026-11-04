using System.Globalization;
using System.Text;
using Lettra.Calculations;
using Lettra.Domain.Parties;
using Lettra.Domain.Reminders;

namespace Lettra.Exports.Application.Reminders;

/// <summary>
/// Tout ce que le texte d'une relance cite. Pour une échéance manquée, <paramref name="DueDate"/> est la date de
/// l'échéance et <paramref name="Outstanding"/> ce qui manque à l'échéancier.
/// </summary>
public sealed record ReminderLetter(
    ReminderLevel Level,
    string Language,
    string CustomerName,
    string InvoiceNumber,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal Outstanding,
    int DaysOverdue,
    decimal? PenaltyRatePercent,
    LatePenalty Penalty,
    ReminderSender Sender,
    ReminderHelp? Help = null);

/// <summary>
/// Ce qu'ajoutent le rappel avant échéance et la relance d'échéance manquée : le lien de l'espace client (ou, dans
/// l'aperçu, la mention qu'il sera créé à l'envoi), le QR code de virement joint, l'échéancier qu'un client
/// professionnel peut demander, et le calendrier de l'échéancier en cours.
/// </summary>
public sealed record ReminderHelp(
    Uri? PortalUrl,
    bool PortalPlanned,
    bool QrAttached,
    bool CanRequestPlan,
    int? Installment = null,
    IReadOnlyList<PaymentPlanInstallmentState>? Schedule = null);

/// <summary>
/// Modèles des relances, en français et en anglais (la langue du client ; français par défaut). Le ton monte d'un
/// niveau de l'escalade à l'autre ; les pénalités de retard et l'indemnité forfaitaire de 40 € ne sont citées qu'à la
/// dernière relance et à la mise en demeure. Le rappel avant échéance et la relance d'échéance manquée gardent un ton
/// amical et donnent les moyens de payer. Textes à faire relire par l'expert-comptable (jalon J2).
/// </summary>
public static class ReminderTemplates
{
    public const string English = DocumentLanguages.English;

    public const string French = DocumentLanguages.French;

    /// <summary><c>en-US</c> pour un client anglophone, <c>fr-FR</c> sinon.</summary>
    public static string LanguageOf(string? preferred) =>
        preferred is not null && preferred.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : French;

    public static (string Subject, string Body) Write(ReminderLetter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return (letter.Level, letter.Language == English) switch
        {
            (ReminderLevel.BeforeDue or ReminderLevel.MissedInstallment, false) => WriteFrenchHelp(letter),
            (ReminderLevel.BeforeDue or ReminderLevel.MissedInstallment, true) => WriteEnglishHelp(letter),
            (_, true) => WriteEnglish(letter),
            _ => WriteFrench(letter),
        };
    }

    private static (string Subject, string Body) WriteFrenchHelp(ReminderLetter l)
    {
        CultureInfo fr = CultureInfo.GetCultureInfo(French);
        ReminderHelp help = l.Help ?? new ReminderHelp(null, false, false, false);
        string amount = Money(l.Outstanding, fr);
        string due = l.DueDate.ToString("d MMMM yyyy", fr);
        string issued = l.IssueDate.ToString("d MMMM yyyy", fr);
        StringBuilder body = new("Bonjour,\n\n");
        string subject;
        if (l.Level == ReminderLevel.BeforeDue)
        {
            subject = $"Votre facture {l.InvoiceNumber} arrive à échéance le {due}";
            body.Append(fr, $"Petit rappel avant l'échéance : la facture {l.InvoiceNumber} du {issued}, d'un montant restant dû de {amount}, est à régler au plus tard le {due}.\n");
        }
        else
        {
            subject = $"Échéance {help.Installment} de votre échéancier : facture {l.InvoiceNumber}";
            body.Append(fr, $"Sauf erreur de notre part, l'échéance {help.Installment} de l'échéancier de la facture {l.InvoiceNumber}, prévue le {due}, n'est pas encore réglée : il reste {amount} à payer pour être à jour.\n");
            AppendSchedule(body, help, "Rappel du calendrier :", "échéance", "réglée", fr);
        }

        if (help.PortalUrl is not null || help.PortalPlanned)
        {
            string link = help.PortalUrl?.ToString() ?? "(lien créé à l'envoi, valable 30 jours)";
            body.Append(fr, $"\nVotre espace client, pour payer en ligne ou retrouver vos factures : {link}\n");
            if (help.CanRequestPlan)
            {
                body.Append("Si vous ne pouvez pas tout régler d'un coup, vous pouvez y demander un échéancier.\n");
            }
        }

        AppendPayment(body, l.Sender, "Pour régler par virement :", fr, l.InvoiceNumber, "en indiquant la référence");
        if (help.QrAttached && l.Sender.Iban is not null)
        {
            body.Append("Le QR code joint ouvre ce virement déjà rempli dans votre application bancaire.\n");
        }

        body.Append(fr, $"\nSi le paiement est déjà parti, merci de ne pas tenir compte de ce message.\n\nCordialement,\n{l.Sender.Name}\n");
        return (subject, body.ToString());
    }

    private static (string Subject, string Body) WriteEnglishHelp(ReminderLetter l)
    {
        CultureInfo en = CultureInfo.GetCultureInfo("en-GB");
        ReminderHelp help = l.Help ?? new ReminderHelp(null, false, false, false);
        string amount = Money(l.Outstanding, en);
        string due = l.DueDate.ToString("d MMMM yyyy", en);
        string issued = l.IssueDate.ToString("d MMMM yyyy", en);
        StringBuilder body = new("Hello,\n\n");
        string subject;
        if (l.Level == ReminderLevel.BeforeDue)
        {
            subject = $"Your invoice {l.InvoiceNumber} is due on {due}";
            body.Append(en, $"A friendly reminder before the due date: invoice {l.InvoiceNumber} dated {issued}, with {amount} outstanding, is due on {due}.\n");
        }
        else
        {
            subject = $"Instalment {help.Installment} of your payment plan: invoice {l.InvoiceNumber}";
            body.Append(en, $"Our records show that instalment {help.Installment} of the payment plan for invoice {l.InvoiceNumber}, due on {due}, has not been paid yet: {amount} is needed to be up to date.\n");
            AppendSchedule(body, help, "Your payment plan:", "instalment", "paid", en);
        }

        if (help.PortalUrl is not null || help.PortalPlanned)
        {
            string link = help.PortalUrl?.ToString() ?? "(link created when sent, valid for 30 days)";
            body.Append(en, $"\nYour customer area, to pay online or find your invoices: {link}\n");
            if (help.CanRequestPlan)
            {
                body.Append("If you cannot pay it all at once, you can ask for a payment plan there.\n");
            }
        }

        AppendPayment(body, l.Sender, "To pay by bank transfer:", en, l.InvoiceNumber, "quoting reference");
        if (help.QrAttached && l.Sender.Iban is not null)
        {
            body.Append("The attached QR code opens this transfer, already filled in, in your banking app.\n");
        }

        body.Append(en, $"\nIf you have already paid, please disregard this message.\n\nKind regards,\n{l.Sender.Name}\n");
        return (subject, body.ToString());
    }

    private static void AppendSchedule(StringBuilder body, ReminderHelp help, string intro, string label, string paid, CultureInfo culture)
    {
        if (help.Schedule is not { Count: > 0 } schedule)
        {
            return;
        }

        body.Append(culture, $"\n{intro}\n");
        foreach (PaymentPlanInstallmentState state in schedule)
        {
            PaymentPlanInstallment i = state.Installment;
            body.Append(culture, $"- {label} {i.Number}, {i.DueDate.ToString("d", culture)}, {Money(i.Amount, culture)}{(state.Paid ? $" ({paid})" : "")}\n");
        }
    }

    private static (string Subject, string Body) WriteFrench(ReminderLetter l)
    {
        CultureInfo fr = CultureInfo.GetCultureInfo(French);
        string amount = Money(l.Outstanding, fr);
        string due = l.DueDate.ToString("d MMMM yyyy", fr);
        string issued = l.IssueDate.ToString("d MMMM yyyy", fr);
        string subject = l.Level switch
        {
            ReminderLevel.Friendly => $"Rappel : facture {l.InvoiceNumber} arrivée à échéance",
            ReminderLevel.Reminder => $"Relance : facture {l.InvoiceNumber} impayée",
            ReminderLevel.Final => $"Dernière relance avant mise en demeure : facture {l.InvoiceNumber}",
            _ => $"Mise en demeure de payer : facture {l.InvoiceNumber}",
        };
        StringBuilder body = new();
        body.Append(fr, $"Bonjour,\n\n");
        body.Append(l.Level switch
        {
            ReminderLevel.Friendly => string.Create(fr,
                $"Sauf erreur de notre part, la facture {l.InvoiceNumber} du {issued}, échue le {due}, n'est pas encore réglée. Il s'agit peut-être d'un simple oubli : le montant restant dû est de {amount}.\n"),
            ReminderLevel.Reminder => string.Create(fr,
                $"Malgré notre premier rappel, la facture {l.InvoiceNumber} du {issued}, échue le {due} ({l.DaysOverdue} jours de retard), reste impayée. Le montant restant dû est de {amount}. Nous vous remercions de procéder à son règlement dans les meilleurs délais.\n"),
            ReminderLevel.Final => string.Create(fr,
                $"Malgré nos relances, la facture {l.InvoiceNumber} du {issued}, échue le {due} ({l.DaysOverdue} jours de retard), reste impayée pour {amount}. Sans règlement de votre part sous huit jours, nous vous adresserons une mise en demeure.\n"),
            _ => string.Create(fr,
                $"Malgré nos relances restées sans effet, la facture {l.InvoiceNumber} du {issued}, échue le {due} ({l.DaysOverdue} jours de retard), reste impayée pour {amount}. Par la présente, nous vous mettons en demeure de régler cette somme sous huit jours à compter de la réception de ce message. À défaut, nous pourrons engager une procédure de recouvrement.\n"),
        });
        if (l.Level >= ReminderLevel.Final && l.Penalty.Total > 0m)
        {
            body.Append(fr, $"\nConformément à nos conditions de vente et à l'article L441-10 du Code de commerce, ce retard vous rend redevable");
            if (l.Penalty.Interest > 0m && l.PenaltyRatePercent is decimal rate)
            {
                body.Append(fr, $" de pénalités de retard de {Money(l.Penalty.Interest, fr)} à ce jour (taux annuel de {rate:0.##} %)");
            }

            if (l.Penalty.RecoveryIndemnity > 0m)
            {
                body.Append(fr, $"{(l.Penalty.Interest > 0m ? " et" : "")} de l'indemnité forfaitaire pour frais de recouvrement de {Money(l.Penalty.RecoveryIndemnity, fr)}");
            }

            body.Append(".\n");
        }

        AppendPayment(body, l.Sender, "Pour régler par virement :", fr);
        body.Append(fr, $"\nSi le paiement a été fait entre-temps, merci de ne pas tenir compte de ce message.\n\nCordialement,\n{l.Sender.Name}\n");
        return (subject, body.ToString());
    }

    private static (string Subject, string Body) WriteEnglish(ReminderLetter l)
    {
        CultureInfo en = CultureInfo.GetCultureInfo("en-GB");
        string amount = Money(l.Outstanding, en);
        string due = l.DueDate.ToString("d MMMM yyyy", en);
        string issued = l.IssueDate.ToString("d MMMM yyyy", en);
        string subject = l.Level switch
        {
            ReminderLevel.Friendly => $"Reminder: invoice {l.InvoiceNumber} is now due",
            ReminderLevel.Reminder => $"Second reminder: invoice {l.InvoiceNumber} is unpaid",
            ReminderLevel.Final => $"Final reminder before formal notice: invoice {l.InvoiceNumber}",
            _ => $"Formal notice to pay: invoice {l.InvoiceNumber}",
        };
        StringBuilder body = new();
        body.Append("Hello,\n\n");
        body.Append(l.Level switch
        {
            ReminderLevel.Friendly => string.Create(en,
                $"Our records show that invoice {l.InvoiceNumber} dated {issued}, due on {due}, has not been paid yet. This may simply be an oversight: the amount outstanding is {amount}.\n"),
            ReminderLevel.Reminder => string.Create(en,
                $"Despite our first reminder, invoice {l.InvoiceNumber} dated {issued}, due on {due} ({l.DaysOverdue} days overdue), remains unpaid. The amount outstanding is {amount}. Please arrange payment as soon as possible.\n"),
            ReminderLevel.Final => string.Create(en,
                $"Despite our reminders, invoice {l.InvoiceNumber} dated {issued}, due on {due} ({l.DaysOverdue} days overdue), remains unpaid for {amount}. If we do not receive payment within eight days, we will send you a formal notice.\n"),
            _ => string.Create(en,
                $"Our reminders have gone unanswered: invoice {l.InvoiceNumber} dated {issued}, due on {due} ({l.DaysOverdue} days overdue), remains unpaid for {amount}. We hereby give you formal notice to pay this amount within eight days of receiving this message. Failing that, we may start debt recovery proceedings.\n"),
        });
        if (l.Level >= ReminderLevel.Final && l.Penalty.Total > 0m)
        {
            body.Append("\nUnder our terms of sale and article L441-10 of the French Commercial Code, this delay makes you liable for");
            if (l.Penalty.Interest > 0m && l.PenaltyRatePercent is decimal rate)
            {
                body.Append(en, $" late payment interest of {Money(l.Penalty.Interest, en)} to date (annual rate {rate:0.##}%)");
            }

            if (l.Penalty.RecoveryIndemnity > 0m)
            {
                body.Append(en, $"{(l.Penalty.Interest > 0m ? " and" : "")} the fixed recovery fee of {Money(l.Penalty.RecoveryIndemnity, en)}");
            }

            body.Append(".\n");
        }

        AppendPayment(body, l.Sender, "To pay by bank transfer:", en);
        body.Append(en, $"\nIf you have already paid, please disregard this message.\n\nKind regards,\n{l.Sender.Name}\n");
        return (subject, body.ToString());
    }

    private static void AppendPayment(
        StringBuilder body, ReminderSender sender, string intro, CultureInfo culture, string? reference = null, string? referenceIntro = null)
    {
        if (sender.Iban is null)
        {
            return;
        }

        body.Append(culture, $"\n{intro} IBAN {sender.Iban}");
        if (sender.Bic is not null)
        {
            body.Append(culture, $", BIC {sender.Bic}");
        }

        if (reference is not null)
        {
            body.Append(culture, $", {referenceIntro} {reference}");
        }

        body.Append(".\n");
    }

    /// <summary>« 1 234,56 € » en français, « €1,234.56 » en anglais.</summary>
    internal static string Money(decimal amount, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr"
            ? string.Create(culture, $"{amount:N2} €")
            : string.Create(culture, $"€{amount:N2}");
}
