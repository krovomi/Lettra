using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.Printing;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Prevention;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Prevention;

/// <summary>Les phrases du radar écrites à l'écran à partir des chiffres du serveur, et le courrier mis en lettre pour le PDF.</summary>
[Collection("culture")]
public sealed class PreventionTextTests : BunitContext
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public PreventionTextTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(French);
    }

    private IStringLocalizer<SharedResource> L => Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    /// <summary>Les espaces insécables du français (avant « % ») comparés comme des espaces simples.</summary>
    private static string Plain(string text) => text.Replace('\u00A0', ' ').Replace('\u202F', ' ');

    private static PreventionSignalResponse Signal(PreventionSignalDto signal, PreventionSignalStateDto state, params PreventionFigureDto[] figures) =>
        new(signal, state, PreventionUnavailableDto.None, null, null, figures, [], [], []);

    [Fact]
    public void L_ecart_au_secteur_signe_les_evolutions_mais_pas_le_seuil()
    {
        PreventionSignalResponse gap = Signal(
            PreventionSignalDto.PeerGap,
            PreventionSignalStateDto.Watch,
            new("trend", PreventionFigureKindDto.Percent, -12.5m),
            new("median", PreventionFigureKindDto.Percent, 4m),
            new("gapPoints", PreventionFigureKindDto.Count, 17m),
            new("peers", PreventionFigureKindDto.Count, 6m),
            new("threshold", PreventionFigureKindDto.Count, 15m));

        Plain(PreventionText.Sentence(L, gap, French)).Should().Be(
            "Le chiffre d'affaires évolue de −12,5 % sur un an, contre +4,0 % pour la médiane de 6 dossiers du même secteur : 17 points d'écart (seuil 15).");
    }

    [Fact]
    public void Le_premier_client_se_lit_avec_sa_part_et_le_seuil_en_pourcentage()
    {
        PreventionSignalResponse top = Signal(
            PreventionSignalDto.TopCustomer,
            PreventionSignalStateDto.Clear,
            new("customer", PreventionFigureKindDto.Text, Text: "Garage du Centre"),
            new("share", PreventionFigureKindDto.Percent, 42.3m),
            new("threshold", PreventionFigureKindDto.Percent, 50m),
            new("revenue", PreventionFigureKindDto.Amount, 4_230m),
            new("total", PreventionFigureKindDto.Amount, 10_000m));

        Plain(PreventionText.Sentence(L, top, French)).Should().Be("Le premier client, Garage du Centre, pèse 42,3 % du chiffre d'affaires des douze derniers mois (seuil 50 %).");
    }

    [Fact]
    public void Une_obligation_inconnue_garde_son_code_et_une_date_manquante_reste_vide()
    {
        PreventionSignalResponse vat = Signal(
            PreventionSignalDto.VatBeyondCollections,
            PreventionSignalStateDto.Clear,
            new("obligation", PreventionFigureKindDto.Text, Text: "Futur"),
            new("dueDate", PreventionFigureKindDto.Date),
            new("vat", PreventionFigureKindDto.Amount, 0m),
            new("collections", PreventionFigureKindDto.Amount, 0m),
            new("periodStart", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 10, 1)),
            new("periodEnd", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 10, 31)));

        PreventionText.Sentence(L, vat, French).Should().StartWith("Futur, période du 1 octobre 2026 au 31 octobre 2026 : ").And.Contain(" à payer le , ");
    }

    [Fact]
    public void Une_echeance_sans_libelle_propre_au_signal_prend_le_libelle_commun()
    {
        PreventionText.DueOn(L, PreventionSignalDto.PaymentDelay, new DateOnly(2026, 11, 3), French).Should().Be("le 3 novembre 2026");
        PreventionText.DueOn(L, PreventionSignalDto.VatBeyondCollections, new DateOnly(2026, 11, 24), French).Should().Be("à payer le 24 novembre 2026");
    }

    [Fact]
    public void Chaque_signal_a_son_icone_son_titre_et_sa_couleur_d_etat()
    {
        PreventionText.Signals.Select(PreventionText.Icon).Should().OnlyHaveUniqueItems().And.NotContain("radar");
        PreventionText.Signals.Select(s => PreventionText.Title(L, s)).Should().OnlyHaveUniqueItems().And.NotContain(t => t.StartsWith("Prevention.", StringComparison.Ordinal));
        Enum.GetValues<PreventionSignalStateDto>().Select(PreventionText.Tone).Should().Equal("Calm", "Watch", "Call", "Muted", "Muted");
        PreventionText.Icon((PreventionSignalDto)99).Should().Be("radar");
    }

    [Fact]
    public void Les_chiffres_suivent_la_langue_de_l_ecran()
    {
        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        LettraCultures.Apply(english);

        PreventionText.Days(L, 45m, english).Should().Be("45 d");
        PreventionText.SignedPercent(-3.26m, english).Should().Be("−3.3%");
        PreventionText.SignedPercent(0.04m, english).Should().Be("0.0%");
        PreventionText.Percent(57.94m, english).Should().Be("57.9%");
        PreventionText.Percent(49.96m, english).Should().Be("50%");
        PreventionText.LongDate(new DateOnly(2026, 11, 3), english).Should().Be("3 November 2026");
        PreventionText.Level(L, PreventionLevelDto.Watch).Should().Be("Watch");
    }

    [Fact]
    public void Le_courrier_en_pdf_garde_un_paragraphe_par_ligne_et_date_dans_sa_langue()
    {
        PrintableLetter letter = PreventionLetterPdf.Compose(
            "en-US", " Boulangerie Martin ", " Meeting proposal ", "Dear Sir or Madam,\r\n\r\nWe follow your business.\n- cash\n- payment times\n", new DateOnly(2026, 11, 3));

        letter.Culture.Name.Should().Be("en-US");
        letter.Sender.Should().BeEmpty();
        letter.Recipient.Should().Equal("Boulangerie Martin");
        letter.Dateline.Should().Be("Tuesday, November 3, 2026");
        letter.Title.Should().Be("Meeting proposal");
        letter.Sections.Should().ContainSingle().Which.Paragraphs.Should().Equal("Dear Sir or Madam,", "We follow your business.", "- cash", "- payment times");
    }

    [Fact]
    public void Un_courrier_sans_langue_ni_entreprise_part_en_francais_sans_destinataire()
    {
        PrintableLetter letter = PreventionLetterPdf.Compose("", " ", "Objet", "Texte", new DateOnly(2026, 11, 3));

        letter.Culture.Name.Should().Be("fr-FR");
        letter.Recipient.Should().BeEmpty();
        letter.Dateline.Should().Be("mardi 3 novembre 2026");
        PreventionLetterPdf.FileName(new DateOnly(2026, 11, 3)).Should().Be("rendez-vous-prevention-2026-11-03.pdf");
    }
}
