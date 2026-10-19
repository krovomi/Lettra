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
}
