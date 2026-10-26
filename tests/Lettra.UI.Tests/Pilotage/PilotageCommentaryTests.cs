using System.Globalization;
using Bunit;
using Lettra.Contracts.Pilotage;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Pilotage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Pilotage;

/// <summary>
/// Le commentaire écrit des tableaux de bord : des phrases tirées des mêmes chiffres et des mêmes seuils que les
/// vignettes, au singulier comme au pluriel, en français et en anglais. Les mêmes chiffres donnent le même texte.
/// </summary>
[Collection("culture")]
public sealed class PilotageCommentaryTests : BunitContext
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public PilotageCommentaryTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(French);
    }

    private IStringLocalizer L => Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    private static string Plain(string text) => text.Replace(' ', ' ').Replace(' ', ' ');

    private static DossierPilotageDto Dossier(
        double? dso = 38, double? overdue = 0.12, double? collection = 0.91, double? trend = 0.08, double? top = 0.35, double? conversion = 0.6, int pending = 4, int expiring = 1) =>
        new(Guid.CreateVersion7(), "Boulangerie Martin", dso, overdue, collection, trend, 24, top, conversion, pending, expiring, Months());

    private static MonthlyActivityDto[] Months(params double?[] index) =>
    [
        .. Enumerable.Range(0, 12).Select(i => new MonthlyActivityDto(
            new DateOnly(2025, 11, 1).AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture),
            index.Length == 12 ? index[i] : 100,
            10 + i)),
    ];

    [Fact]
    public void Les_clients_en_phrases_avec_les_seuils_des_vignettes()
    {
        Plain(PilotageCommentary.Clients(Dossier(), L, French)).Should().Be(
            "Le délai moyen de paiement des clients est de 38 jours : c'est un bon délai. 12 % de ce qui reste dû est en retard. "
            + "91 % de ce qui a été facturé sur 12 mois est encaissé. "
            + "Le premier client pèse 35 % du chiffre d'affaires, parmi 24 clients actifs : une dépendance à surveiller.");
        Plain(PilotageCommentary.Clients(Dossier(dso: 75, overdue: 0, collection: 0.5, top: null), L, French)).Should().Be(
            "Le délai moyen de paiement des clients est de 75 jours : c'est trop long, il dépasse 60 jours. Aucune créance n'est en retard. "
            + "Seulement 50 % de ce qui a été facturé sur 12 mois est encaissé : la trésorerie en souffre.");
        PilotageCommentary.Clients(Dossier(dso: null, overdue: null, collection: null, top: null), L, French)
            .Should().Be("Pas encore assez de pièces pour commenter ces chiffres.");
    }

    [Theory]
    [InlineData(0.08, 4, 1, "Le chiffre d'affaires des trois derniers mois est en hausse de 8 % sur les trois précédents. 60 % des devis tranchés ont été acceptés. 4 devis attendent une réponse ; à relancer avant expiration : 1.")]
    [InlineData(-0.15, 1, 0, "Le chiffre d'affaires des trois derniers mois est en baisse de 15 % sur les trois précédents. 60 % des devis tranchés ont été acceptés. Un devis attend une réponse.")]
    [InlineData(0.01, 0, 0, "Le chiffre d'affaires des trois derniers mois est stable par rapport aux trois précédents. 60 % des devis tranchés ont été acceptés. Aucun devis n'attend de réponse.")]
    public void Les_ventes_disent_la_tendance_et_les_devis(double trend, int pending, int expiring, string expected)
    {
        Plain(PilotageCommentary.Sales(Dossier(trend: trend, pending: pending, expiring: expiring), L, French)).Should().Be(expected);
    }

    [Fact]
    public void L_activite_commente_le_dernier_mois_complet_et_le_meilleur_mois()
    {
        MonthlyActivityDto[] months = Months(80, 90, 140, 95, 100, 105, 90, 85, 110, 95, 120, 60);

        Plain(PilotageCommentary.Activity(months, L, French)).Should().Be(
            "En septembre 2026, l'activité est à l'indice 120 : 20 % au-dessus du mois moyen de l'année. Le meilleur mois de l'année est janvier 2026 (indice 140).");
        Plain(PilotageCommentary.Activity(Months(80, 90, 100, 95, 100, 105, 90, 85, 110, 95, 150, 60), L, French))
            .Should().EndWith("C'est le meilleur mois de l'année.");
        PilotageCommentary.Activity(Months(null, null, null, null, null, null, null, null, null, null, null, null), L, French)
            .Should().Be("Aucune vente sur les douze derniers mois.");
    }

    [Fact]
    public void Les_factures_comparent_les_deux_derniers_mois_complets()
    {
        Plain(PilotageCommentary.Invoices(Months(), L, French)).Should().Be(
            "20 factures émises en septembre 2026, contre 19 en août 2026. 186 au total sur les douze derniers mois.");
    }

    [Fact]
    public void Mes_dossiers_dit_combien_appellent_une_attention()
    {
        DossierPilotageDto good = Dossier();
        DossierPilotageDto late = Dossier(dso: 50, overdue: 0.45, trend: -0.2);

        Plain(PilotageCommentary.Cabinet([good, good], L, French)).Should().Be(
            "2 dossiers suivis. Aucun n'appelle d'attention : retards, délais et activité sont dans la norme.");
        Plain(PilotageCommentary.Cabinet([good, late], L, French)).Should().Be(
            "2 dossiers suivis. Un dossier a plus de 20 % de créances en retard. Un dossier a un délai de paiement de plus de 45 jours. "
            + "Un dossier voit son chiffre d'affaires baisser de plus de 10 %.");
    }

    [Fact]
    public void Le_commentaire_s_ecrit_aussi_en_anglais()
    {
        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        LettraCultures.Apply(english);

        PilotageCommentary.Sales(Dossier(), L, english).Should().Be(
            "Revenue over the last three months is up 8% on the three before. 60% of decided quotes were accepted. 4 quotes are awaiting an answer; to chase before they expire: 1.");
    }
}
