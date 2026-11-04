using Lettra.Calculations;
using Lettra.Domain.Reminders;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Reminders;

public sealed class ReminderTemplatesTests
{
    private static readonly ReminderSender Sender = new("Atelier Martin", "FR76 3000 6000 0112 3456 7890 189", "AGRIFRPP");

    private static ReminderLetter Letter(ReminderLevel level, string language, LatePenalty penalty = default, decimal? rate = 12m, ReminderSender? sender = null) => new(
        level, language, "Durand SARL", "F-2026-0042", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 1234.56m, 50, rate, penalty, sender ?? Sender);

    [Theory]
    [InlineData(null, ReminderTemplates.French)]
    [InlineData("fr-FR", ReminderTemplates.French)]
    [InlineData("de-DE", ReminderTemplates.French)]
    [InlineData("en-GB", ReminderTemplates.English)]
    [InlineData("EN", ReminderTemplates.English)]
    public void La_langue_du_client_choisit_le_modele(string? preferred, string expected) =>
        ReminderTemplates.LanguageOf(preferred).Should().Be(expected);

    [Theory]
    [InlineData(ReminderLevel.Friendly, "Rappel : facture F-2026-0042 arrivée à échéance", "Il s'agit peut-être d'un simple oubli")]
    [InlineData(ReminderLevel.Reminder, "Relance : facture F-2026-0042 impayée", "Malgré notre premier rappel")]
    [InlineData(ReminderLevel.Final, "Dernière relance avant mise en demeure : facture F-2026-0042", "sous huit jours, nous vous adresserons une mise en demeure")]
    [InlineData(ReminderLevel.FormalNotice, "Mise en demeure de payer : facture F-2026-0042", "nous vous mettons en demeure")]
    public void Chaque_niveau_a_son_objet_et_son_ton_en_francais(ReminderLevel level, string subject, string tone)
    {
        (string s, string body) = ReminderTemplates.Write(Letter(level, ReminderTemplates.French));

        s.Should().Be(subject);
        body.Should().StartWith("Bonjour,").And.Contain(tone).And.Contain("234,56 €").And.Contain("31 août 2026")
            .And.Contain("IBAN FR76 3000 6000 0112 3456 7890 189, BIC AGRIFRPP.").And.EndWith("Cordialement,\nAtelier Martin\n");
    }

    [Theory]
    [InlineData(ReminderLevel.Friendly, "Reminder: invoice F-2026-0042 is now due", "simply be an oversight")]
    [InlineData(ReminderLevel.Reminder, "Second reminder: invoice F-2026-0042 is unpaid", "Despite our first reminder")]
    [InlineData(ReminderLevel.Final, "Final reminder before formal notice: invoice F-2026-0042", "we will send you a formal notice")]
    [InlineData(ReminderLevel.FormalNotice, "Formal notice to pay: invoice F-2026-0042", "We hereby give you formal notice")]
    public void Chaque_niveau_a_son_objet_et_son_ton_en_anglais(ReminderLevel level, string subject, string tone)
    {
        (string s, string body) = ReminderTemplates.Write(Letter(level, ReminderTemplates.English));

        s.Should().Be(subject);
        body.Should().StartWith("Hello,").And.Contain(tone).And.Contain("€1,234.56").And.Contain("31 August 2026")
            .And.Contain("To pay by bank transfer: IBAN").And.EndWith("Kind regards,\nAtelier Martin\n");
    }

    [Theory]
    [InlineData(ReminderLevel.Friendly)]
    [InlineData(ReminderLevel.Reminder)]
    public void Les_premieres_relances_ne_citent_pas_de_penalites(ReminderLevel level)
    {
        LatePenalty penalty = new(16.91m, 40m);

        ReminderTemplates.Write(Letter(level, ReminderTemplates.French, penalty)).Body.Should().NotContain("L441-10");
        ReminderTemplates.Write(Letter(level, ReminderTemplates.English, penalty)).Body.Should().NotContain("L441-10");
    }

    [Fact]
    public void La_derniere_relance_cite_les_interets_et_l_indemnite()
    {
        LatePenalty penalty = new(16.91m, 40m);

        ReminderTemplates.Write(Letter(ReminderLevel.Final, ReminderTemplates.French, penalty)).Body.Should().Contain(
            "de pénalités de retard de 16,91 € à ce jour (taux annuel de 12 %) et de l'indemnité forfaitaire pour frais de recouvrement de 40,00 €.");
        ReminderTemplates.Write(Letter(ReminderLevel.FormalNotice, ReminderTemplates.English, penalty)).Body.Should().Contain(
            "late payment interest of €16.91 to date (annual rate 12%) and the fixed recovery fee of €40.00.");
    }

    [Fact]
    public void Sans_taux_seule_l_indemnite_est_citee_et_sans_indemnite_seuls_les_interets()
    {
        string indemnityOnly = ReminderTemplates.Write(Letter(ReminderLevel.Final, ReminderTemplates.French, new LatePenalty(0m, 40m), rate: null)).Body;
        string interestOnly = ReminderTemplates.Write(Letter(ReminderLevel.Final, ReminderTemplates.English, new LatePenalty(16.91m, 0m))).Body;

        indemnityOnly.Should().Contain("ce retard vous rend redevable de l'indemnité forfaitaire").And.NotContain("pénalités de retard de");
        interestOnly.Should().Contain("liable for late payment interest of €16.91 to date (annual rate 12%).").And.NotContain("recovery fee");
    }

    [Fact]
    public void Sans_iban_le_paragraphe_de_virement_disparait_et_sans_bic_seul_l_iban_reste()
    {
        ReminderTemplates.Write(Letter(ReminderLevel.Reminder, ReminderTemplates.French, sender: new ReminderSender("Atelier Martin", null, null)))
            .Body.Should().NotContain("IBAN");
        ReminderTemplates.Write(Letter(ReminderLevel.Reminder, ReminderTemplates.English, sender: new ReminderSender("Atelier Martin", "FR76 1", null)))
            .Body.Should().Contain("IBAN FR76 1.\n").And.NotContain("BIC");
    }

    [Fact]
    public void Une_lettre_est_obligatoire()
    {
        Action act = () => ReminderTemplates.Write(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ReminderLetter Help(ReminderLevel level, string language, ReminderHelp? help, ReminderSender? sender = null) =>
        Letter(level, language, sender: sender) with { Help = help };

    private static IReadOnlyList<PaymentPlanInstallmentState> Schedule() =>
    [
        new(new PaymentPlanInstallment(1, new DateOnly(2026, 9, 15), 600m), 600m, true, false, 0m),
        new(new PaymentPlanInstallment(2, new DateOnly(2026, 10, 15), 634.56m), 1234.56m, false, true, 634.56m),
    ];

    [Fact]
    public void Le_rappel_avant_echeance_donne_le_lien_l_echeancier_et_le_virement_en_francais()
    {
        (string subject, string body) = ReminderTemplates.Write(Help(
            ReminderLevel.BeforeDue, ReminderTemplates.French, new ReminderHelp(new Uri("https://lettra.example/espace-client/abc"), true, true, true)));

        subject.Should().Be("Votre facture F-2026-0042 arrive à échéance le 31 août 2026");
        body.Should().StartWith("Bonjour,\n\nPetit rappel avant l'échéance : la facture F-2026-0042 du 1 août 2026")
            .And.Contain("restant dû de 1\u202f234,56 €")
            .And.Contain("Votre espace client, pour payer en ligne ou retrouver vos factures : https://lettra.example/espace-client/abc")
            .And.Contain("vous pouvez y demander un échéancier")
            .And.Contain("IBAN FR76 3000 6000 0112 3456 7890 189, BIC AGRIFRPP, en indiquant la référence F-2026-0042")
            .And.Contain("Le QR code joint")
            .And.EndWith("Cordialement,\nAtelier Martin\n");
        body.Should().NotContain("L441-10", "un rappel avant l'échéance ne parle jamais de pénalités");
    }

    [Fact]
    public void Dans_l_apercu_le_lien_est_annonce_et_rien_n_est_promis_sans_iban()
    {
        string body = ReminderTemplates.Write(Help(
            ReminderLevel.BeforeDue, ReminderTemplates.French, new ReminderHelp(null, true, true, false), Sender with { Iban = null })).Body;

        body.Should().Contain("(lien créé à l'envoi, valable 30 jours)").And.NotContain("échéancier").And.NotContain("QR code").And.NotContain("IBAN");
    }

    [Fact]
    public void Sans_espace_client_ni_aide_le_rappel_reste_simple()
    {
        string body = ReminderTemplates.Write(Help(ReminderLevel.BeforeDue, ReminderTemplates.English, null)).Body;

        body.Should().NotContain("customer area").And.NotContain("QR code").And.Contain("To pay by bank transfer: IBAN");
    }

    [Fact]
    public void Le_rappel_avant_echeance_en_anglais()
    {
        (string subject, string body) = ReminderTemplates.Write(Help(
            ReminderLevel.BeforeDue, ReminderTemplates.English, new ReminderHelp(null, true, true, true)));

        subject.Should().Be("Your invoice F-2026-0042 is due on 31 August 2026");
        body.Should().StartWith("Hello,\n\nA friendly reminder before the due date: invoice F-2026-0042 dated 1 August 2026, with €1,234.56 outstanding")
            .And.Contain("(link created when sent, valid for 30 days)")
            .And.Contain("you can ask for a payment plan there")
            .And.Contain("quoting reference F-2026-0042")
            .And.Contain("The attached QR code opens this transfer")
            .And.EndWith("Kind regards,\nAtelier Martin\n");
    }

    [Fact]
    public void La_relance_d_echeance_manquee_rappelle_le_calendrier_en_francais()
    {
        ReminderLetter letter = Help(ReminderLevel.MissedInstallment, ReminderTemplates.French, new ReminderHelp(null, false, false, false, 2, Schedule()));
        (string subject, string body) = ReminderTemplates.Write(letter with { Outstanding = 634.56m });

        subject.Should().Be("Échéance 2 de votre échéancier : facture F-2026-0042");
        body.Should().Contain("l'échéance 2 de l'échéancier de la facture F-2026-0042, prévue le 31 août 2026, n'est pas encore réglée : il reste 634,56 €")
            .And.Contain("Rappel du calendrier :\n- échéance 1, 15/09/2026, 600,00 € (réglée)\n- échéance 2, 15/10/2026, 634,56 €\n")
            .And.NotContain("espace client").And.NotContain("QR code");
    }

    [Fact]
    public void La_relance_d_echeance_manquee_en_anglais()
    {
        (string subject, string body) = ReminderTemplates.Write(Help(
            ReminderLevel.MissedInstallment, ReminderTemplates.English, new ReminderHelp(new Uri("https://lettra.example/espace-client/abc"), true, true, false, 2, Schedule())));

        subject.Should().Be("Instalment 2 of your payment plan: invoice F-2026-0042");
        body.Should().Contain("instalment 2 of the payment plan for invoice F-2026-0042, due on 31 August 2026, has not been paid yet")
            .And.Contain("Your payment plan:\n- instalment 1, 15/09/2026, €600.00 (paid)\n- instalment 2, 15/10/2026, €634.56\n")
            .And.Contain("Your customer area, to pay online or find your invoices: https://lettra.example/espace-client/abc")
            .And.NotContain("ask for a payment plan");
    }

    [Fact]
    public void Une_relance_d_echeance_sans_aide_ni_calendrier_reste_lisible() =>
        ReminderTemplates.Write(Help(ReminderLevel.MissedInstallment, ReminderTemplates.French, null)).Body
            .Should().NotContain("Rappel du calendrier").And.Contain("n'est pas encore réglée");
}
