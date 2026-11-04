using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Billing;

public sealed class QuoteFollowUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly CustomerId Client = new(Guid.CreateVersion7());

    private static readonly PartySnapshot Martin = new(
        "Boulangerie Martin", "1 rue de la Paix", null, "75002", "Paris", "FR", "732829320", "73282932000074", "FR44732829320",
        null, null, null, IsBusiness: true);

    private static QuoteFollowUpDraft Draft(string subject = "Votre devis DEV-000001", string body = "Bonjour,") =>
        new(QuoteId.New(), Client, "DEV-000001", DocumentLanguages.French, subject, body, 1_200m, Today.AddDays(3));

    private static Quote ValidatedQuote()
    {
        LineDraft line = new(null, null, "Conseil", null, CatalogItemKind.Service, "HUR", 1m, 100m, 0m, "FR-20", null);
        Quote quote = Quote.Create(QuoteId.New(), new QuoteDraft(Client, Martin, null, VatRegime.Standard, null, null, [line]), User, Now).Value;
        quote.Validate("DEV-000001", Today, Martin, User, Now).IsSuccess.Should().BeTrue();
        return quote;
    }

    [Fact]
    public void Une_relance_partie_garde_le_devis_le_texte_et_le_jour()
    {
        QuoteFollowUpDraft draft = Draft();

        QuoteFollowUp followUp = QuoteFollowUp.Record(draft, QuoteFollowUpChannel.Email, "compta@martin.fr", null, User, Now);

        followUp.Outcome.Should().Be(QuoteFollowUpOutcome.Sent);
        followUp.ErrorCode.Should().BeNull();
        (followUp.QuoteId, followUp.CustomerId, followUp.QuoteNumber).Should().Be((draft.QuoteId, Client, "DEV-000001"));
        (followUp.Channel, followUp.Recipient, followUp.Language).Should().Be((QuoteFollowUpChannel.Email, "compta@martin.fr", DocumentLanguages.French));
        (followUp.Subject, followUp.Body).Should().Be((draft.Subject, draft.Body));
        (followUp.AmountExclTax, followUp.ValidUntil).Should().Be((1_200m, Today.AddDays(3)));
        (followUp.UserId, followUp.OccurredAt, followUp.OccurredOn).Should().Be((User, Now, Today));
    }

    [Fact]
    public void Un_envoi_en_echec_est_garde_avec_son_code()
    {
        QuoteFollowUp followUp = QuoteFollowUp.Record(Draft(), QuoteFollowUpChannel.Email, "compta@martin.fr", "Email.SendFailed", User, Now);

        followUp.Outcome.Should().Be(QuoteFollowUpOutcome.Failed);
        followUp.ErrorCode.Should().Be("Email.SendFailed");
    }

    [Fact]
    public void Un_texte_trop_long_est_coupe_a_la_longueur_gardee()
    {
        QuoteFollowUp followUp = QuoteFollowUp.Record(
            Draft(new string('o', 400), new string('b', 9_000)), QuoteFollowUpChannel.Manual, null, null, User, Now);

        followUp.Subject.Should().HaveLength(QuoteFollowUp.MaxSubjectLength);
        followUp.Body.Should().HaveLength(QuoteFollowUp.MaxBodyLength);
    }

    [Fact]
    public void Un_brouillon_absent_est_refuse() =>
        FluentActions.Invoking(() => QuoteFollowUp.Record(null!, QuoteFollowUpChannel.Manual, null, null, User, Now)).Should().Throw<ArgumentNullException>();

    [Theory]
    [InlineData("Objet", "Texte", true)]
    [InlineData("", "Texte", false)]
    [InlineData("  ", "Texte", false)]
    [InlineData("Objet\nsur deux lignes", "Texte", false)]
    [InlineData("Objet", "", false)]
    public void L_objet_et_le_texte_sont_obligatoires(string subject, string body, bool valid) =>
        QuoteFollowUp.CheckMessage(subject, body).IsSuccess.Should().Be(valid);

    [Fact]
    public void Un_objet_et_un_texte_trop_longs_sont_refuses_chacun_avec_sa_cible()
    {
        Result checkedMessage = QuoteFollowUp.CheckMessage(new string('o', 301), new string('b', 8_001));

        checkedMessage.Errors.Select(e => (e.Code, e.Target)).Should().Equal(
            (QuoteFollowUpErrors.MessageInvalidCode, "Subject"),
            (QuoteFollowUpErrors.MessageInvalidCode, "Body"));
    }

    [Fact]
    public void La_fenetre_par_defaut_est_de_cinq_jours()
    {
        QuoteFollowUpSettings settings = QuoteFollowUpSettings.Default();

        (settings.Id, settings.WindowDays, settings.Version).Should().Be((QuoteFollowUpSettings.SingletonId, 5, 0L));
    }

    [Fact]
    public void Un_reglage_valide_change_la_fenetre_et_la_version()
    {
        QuoteFollowUpSettings settings = QuoteFollowUpSettings.Default();

        settings.Update(10, User, Now).IsSuccess.Should().BeTrue();

        (settings.WindowDays, settings.Version, settings.UpdatedBy, settings.UpdatedAt).Should().Be((10, 1L, User, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Une_fenetre_hors_de_1_a_30_jours_est_refusee(int days)
    {
        QuoteFollowUpSettings settings = QuoteFollowUpSettings.Default();

        settings.Update(days, User, Now).Errors.Should().ContainSingle().Which.Should().Match<Error>(
            e => e.Code == QuoteFollowUpErrors.WindowInvalidCode && e.Target == "WindowDays");
        (settings.WindowDays, settings.Version).Should().Be((5, 0L));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public void La_fenetre_va_d_aujourd_hui_a_n_jours(int daysLeft, bool inWindow) =>
        QuoteFollowUpPolicy.IsInWindow(Today.AddDays(daysLeft), Today, 5).Should().Be(inWindow);

    [Fact]
    public void Sans_fin_de_validite_un_devis_n_est_jamais_dans_la_fenetre()
    {
        QuoteFollowUpPolicy.DaysLeft(null, Today).Should().BeNull();
        QuoteFollowUpPolicy.IsInWindow(null, Today, 5).Should().BeFalse();
    }

    [Fact]
    public void Un_devis_envoye_sans_reponse_dans_la_fenetre_est_a_relancer() =>
        QuoteFollowUpPolicy.IsDue(QuoteStatus.Validated, Today.AddDays(3), Today, 5, null).Should().BeTrue();

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Accepted)]
    [InlineData(QuoteStatus.Refused)]
    [InlineData(QuoteStatus.Invoiced)]
    public void Un_devis_qui_n_attend_pas_de_reponse_n_est_pas_a_relancer(QuoteStatus status) =>
        QuoteFollowUpPolicy.IsDue(status, Today.AddDays(3), Today, 5, null).Should().BeFalse();

    [Fact]
    public void Un_devis_relance_depuis_son_entree_dans_la_fenetre_ne_l_est_plus()
    {
        DateOnly validUntil = Today.AddDays(3);

        QuoteFollowUpPolicy.IsDue(QuoteStatus.Validated, validUntil, Today, 5, validUntil.AddDays(-5)).Should().BeFalse();
        QuoteFollowUpPolicy.IsDue(QuoteStatus.Validated, validUntil, Today, 5, validUntil.AddDays(-6)).Should().BeTrue();
    }

    [Fact]
    public void Un_devis_garde_le_jour_de_la_reponse_du_client()
    {
        Quote quote = ValidatedQuote();
        quote.IsPending(Today).Should().BeTrue();
        quote.RespondedOn.Should().BeNull();

        quote.SetOutcome(true, User, Now.AddDays(2)).IsSuccess.Should().BeTrue();

        quote.RespondedOn.Should().Be(Today.AddDays(2));
        quote.IsPending(Today).Should().BeFalse();
    }

    [Fact]
    public void Un_devis_expire_n_attend_plus_de_reponse()
    {
        Quote quote = ValidatedQuote();

        quote.IsPending(quote.ValidUntil!.Value).Should().BeTrue();
        quote.IsPending(quote.ValidUntil!.Value.AddDays(1)).Should().BeFalse();
    }
}
