using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Tests;

/// <summary>L'écran « Anti-blanchiment » et son indicateur dans la vue cabinet : état de la fiche, échéance, conversions.</summary>
public sealed class VigilanceOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Reviewed = new(2026, 1, 15);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    private static ClientDueDiligence File(RiskLevel level = RiskLevel.Standard, IdentityEvidence evidence = IdentityEvidence.Required, DateOnly? ended = null) =>
        ClientDueDiligence.Create(
            new ClientDueDiligence.Draft(evidence, Reviewed, [new("Claire Martin", 75m, OwnershipKind.Capital)], level, RiskFactors.None,
                level == RiskLevel.Standard ? null : "Motif.", Reviewed, ended),
            Reviewer, Reviewed, Now).Value;

    [Fact]
    public void Sans_fiche_ou_avec_une_fiche_incomplete_la_vue_cabinet_signale_le_dossier()
    {
        DateOnly today = new(2026, 10, 26);

        DueDiligenceOverview.Indicator(null, today).Should().Be(new DossierIndicator(DossierIndicatorKeys.ClientDueDiligence, 1, IndicatorLevel.Warning));
        DueDiligenceOverview.Indicator(File(evidence: IdentityEvidence.CompanyRegistration), today)!.Level.Should().Be(IndicatorLevel.Warning);
        DueDiligenceOverview.Indicator(File(), today).Should().BeNull("à jour, revue dans plus de trente jours");
        DueDiligenceOverview.Indicator(File(ended: Reviewed), today).Should().BeNull("relation terminée");
    }

    [Fact]
    public void La_revue_s_annonce_un_mois_avant_et_devient_critique_en_retard_pour_un_risque_eleve()
    {
        ClientDueDiligence high = File(RiskLevel.High);

        DueDiligenceOverview.Indicator(high, new DateOnly(2026, 12, 15)).Should().BeNull();
        DueDiligenceOverview.Indicator(high, new DateOnly(2026, 12, 16)).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.ClientDueDiligence, 1, IndicatorLevel.Info, new DateOnly(2027, 1, 15)));
        DueDiligenceOverview.Indicator(high, new DateOnly(2027, 1, 15)).Should().Be(
            new DossierIndicator(DossierIndicatorKeys.ClientDueDiligence, 1, IndicatorLevel.Critical, new DateOnly(2027, 1, 15)));
        DueDiligenceOverview.Indicator(File(RiskLevel.Low), new DateOnly(2029, 2, 1))!.Level.Should().Be(IndicatorLevel.Warning);
    }

    [Fact]
    public void L_ecran_montre_la_fiche_son_etat_et_qui_l_a_revue()
    {
        CabinetPeople cabinet = new("Cabinet Durand", new Dictionary<Guid, string> { [Reviewer] = "Jeanne Expert" });

        VigilanceOverviewResponse view = DueDiligenceOverview.Build(File(RiskLevel.High), new DateOnly(2026, 10, 26), cabinet);
        VigilanceOverviewResponse empty = DueDiligenceOverview.Build(null, new DateOnly(2026, 10, 26), CabinetPeople.None);

        view.Status.Should().Be(DueDiligenceStatusDto.UpToDate);
        view.File!.ReviewedByName.Should().Be("Jeanne Expert");
        view.File.Level.Should().Be(RiskLevelDto.High);
        view.File.Evidence.Should().Equal(IdentityEvidenceDto.CompanyRegistration, IdentityEvidenceDto.ManagerIdentity);
        view.File.Owners.Should().ContainSingle().Which.Should().Be(new BeneficialOwnerDto { Name = "Claire Martin", SharePercent = 75m, Kind = OwnershipKindDto.Capital });
        view.File.NextReviewOn.Should().Be(new DateOnly(2027, 1, 15));
        view.File.KeepUntil.Should().BeNull();
        empty.Should().Be(new VigilanceOverviewResponse(null, DueDiligenceStatusDto.Missing));
    }

    [Fact]
    public void La_demande_de_l_ecran_devient_le_brouillon_du_domaine()
    {
        SaveDueDiligenceRequest request = new()
        {
            Evidence = [IdentityEvidenceDto.CompanyRegistration, IdentityEvidenceDto.Statutes],
            IdentityVerifiedOn = Reviewed,
            Owners = [new BeneficialOwnerDto { Name = "Claire Martin", Kind = OwnershipKindDto.LegalRepresentative }],
            Level = RiskLevelDto.High,
            Factors = [RiskFactorDto.CashIntensive, RiskFactorDto.HighRiskCountry],
            Justification = "Motif.",
            ReviewedOn = Reviewed,
        };

        ClientDueDiligence.Draft draft = request.ToDraft();

        draft.Evidence.Should().Be(IdentityEvidence.CompanyRegistration | IdentityEvidence.Statutes);
        draft.Factors.Should().Be(RiskFactors.CashIntensive | RiskFactors.HighRiskCountry);
        draft.Level.Should().Be(RiskLevel.High);
        draft.Owners.Should().Equal(new ClientDueDiligence.OwnerDraft("Claire Martin", null, OwnershipKind.LegalRepresentative));
        draft.RelationshipEndedOn.Should().BeNull();
    }
}
