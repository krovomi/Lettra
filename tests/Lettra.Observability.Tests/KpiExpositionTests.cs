using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Observability;

namespace Lettra.Observability.Tests;

/// <summary>Format texte de Prometheus des indicateurs de pilotage : aide, type, étiquettes échappées, rien hors catalogue.</summary>
public sealed class KpiExpositionTests
{
    private static readonly DossierId Dossier = new(new Guid("0199e3a0-0000-7000-8000-00000000d051"));
    private static readonly KpiDossier Named = new(Dossier, "Boulangerie \"Le Fournil\"\\Sud", "Cabinet de démonstration");

    [Fact]
    public void Chaque_indicateur_porte_son_aide_son_type_et_le_nom_du_dossier()
    {
        KpiSnapshot snapshot = new(DateTimeOffset.UnixEpoch, [Named],
        [
            new KpiSample(KpiMetrics.DossierInfo, 1, Dossier),
            new KpiSample(KpiMetrics.DaysSalesOutstanding, 42.5, Dossier),
            new KpiSample(KpiMetrics.RevenueIndex, 120, Dossier, new KpiLabel(KpiMetrics.Labels.Month, "2026-10")),
            new KpiSample(KpiMetrics.OpenAlerts, 2, Label: new KpiLabel(KpiMetrics.Labels.Severity, "S1")),
        ]);

        string text = KpiExposition.Write(snapshot);

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Contain("# TYPE lettra_kpi_dso_days gauge");
        lines.Should().Contain(l => l.StartsWith("# HELP lettra_kpi_dso_days Délai moyen", StringComparison.Ordinal));
        lines.Should().Contain("lettra_kpi_dossier_info{dossier_id=\"0199e3a0-0000-7000-8000-00000000d051\",dossier=\"Boulangerie \\\"Le Fournil\\\"\\\\Sud\",cabinet=\"Cabinet de démonstration\"} 1");
        lines.Should().Contain("lettra_kpi_dso_days{dossier_id=\"0199e3a0-0000-7000-8000-00000000d051\",dossier=\"Boulangerie \\\"Le Fournil\\\"\\\\Sud\"} 42.5");
        lines.Should().Contain(l => l.StartsWith("lettra_kpi_revenue_index{", StringComparison.Ordinal) && l.EndsWith(",month=\"2026-10\"} 120", StringComparison.Ordinal));
        lines.Should().Contain("lettra_kpi_open_alerts{severity=\"S1\"} 2");
        text.Should().EndWith("\n");
    }

    [Fact]
    public void Un_indicateur_hors_catalogue_une_valeur_non_finie_ou_un_dossier_inconnu_ne_sont_pas_ecrits()
    {
        KpiSnapshot snapshot = new(DateTimeOffset.UnixEpoch, [Named],
        [
            new KpiSample("lettra_kpi_chiffre_affaires_euros", 12_000),
            new KpiSample(KpiMetrics.RevenueTrend, double.NaN, Dossier),
            new KpiSample(KpiMetrics.Collection, double.PositiveInfinity, Dossier),
            new KpiSample(KpiMetrics.ActiveCustomers, 3, new DossierId(Guid.CreateVersion7())),
            new KpiSample(KpiMetrics.CollectionFailures, 0),
        ]);

        string text = KpiExposition.Write(snapshot);

        text.Should().NotContain("chiffre_affaires").And.NotContain("revenue_trend").And.NotContain("collection_ratio").And.NotContain("active_customers");
        text.Should().Contain("lettra_kpi_collect_failures 0\n");
    }

    [Fact]
    public void Une_collecte_vide_ne_produit_aucune_ligne() =>
        KpiExposition.Write(new KpiSnapshot(DateTimeOffset.UnixEpoch, [], [])).Should().BeEmpty();

    [Fact]
    public void Chaque_indicateur_du_catalogue_a_un_nom_prometheus_valide_et_une_aide()
    {
        KpiMetrics.Help.Should().OnlyContain(h => System.Text.RegularExpressions.Regex.IsMatch(h.Key, "^lettra_kpi_[a-z_]+$") && h.Value.Length > 10);
        KpiMetrics.Business.Should().OnlyContain(m => KpiMetrics.Help.ContainsKey(m));
    }
}
