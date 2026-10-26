using Lettra.Domain.Common;
using Lettra.Domain.Vigilance;

namespace Lettra.Domain.Tests.Vigilance;

public sealed class ClientDueDiligenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    private static ClientDueDiligence.Draft Draft(RiskLevel level = RiskLevel.Standard, RiskFactors factors = RiskFactors.None, string? justification = null) =>
        new(
            IdentityEvidence.CompanyRegistration | IdentityEvidence.ManagerIdentity,
            new DateOnly(2026, 10, 1),
            [new(" Claire Martin ", 60m, OwnershipKind.Capital), new("Paul Martin", 40m, OwnershipKind.VotingRights)],
            level,
            factors,
            justification,
            new DateOnly(2026, 10, 1),
            null);

    [Fact]
    public void Une_fiche_complete_est_a_jour_et_sa_revue_suit_le_risque()
    {
        ClientDueDiligence file = ClientDueDiligence.Create(Draft(), Reviewer, Today, Now).Value;

        file.Id.Should().Be(ClientDueDiligence.SingletonId);
        file.Owners.Select(o => o.Name).Should().Equal("Claire Martin", "Paul Martin");
        file.Owners.Select(o => o.Position).Should().Equal(1, 2);
        file.ReviewedBy.Should().Be(Reviewer);
        file.NextReviewOn.Should().Be(new DateOnly(2028, 10, 1), "deux ans pour un risque standard");
        file.KeepUntil.Should().BeNull();
        file.StatusOn(Today).Should().Be(DueDiligenceStatus.UpToDate);
        file.StatusOn(new DateOnly(2028, 10, 1)).Should().Be(DueDiligenceStatus.ReviewDue);
        ClientDueDiligence.ReviewIntervalYears(RiskLevel.High).Should().Be(1);
        ClientDueDiligence.ReviewIntervalYears(RiskLevel.Low).Should().Be(3);
    }

    [Fact]
    public void Sans_kbis_piece_d_identite_date_ou_beneficiaire_la_fiche_est_incomplete()
    {
        ClientDueDiligence.Draft complete = Draft();

        Status(complete with { Evidence = IdentityEvidence.CompanyRegistration | IdentityEvidence.Statutes }).Should().Be(DueDiligenceStatus.Incomplete);
        Status(complete with { IdentityVerifiedOn = null }).Should().Be(DueDiligenceStatus.Incomplete);
        Status(complete with { Owners = [] }).Should().Be(DueDiligenceStatus.Incomplete);
        Status(complete with { RelationshipEndedOn = new DateOnly(2026, 10, 20) }).Should().Be(DueDiligenceStatus.Ended);
        ClientDueDiligence.Create(complete with { RelationshipEndedOn = new DateOnly(2026, 10, 20) }, Reviewer, Today, Now).Value
            .KeepUntil.Should().Be(new DateOnly(2031, 10, 20), "cinq ans après la fin de la relation");

        static DueDiligenceStatus Status(ClientDueDiligence.Draft draft) => ClientDueDiligence.Create(draft, Reviewer, Today, Now).Value.StatusOn(Today);
    }

    [Fact]
    public void Une_personne_politiquement_exposee_impose_le_risque_eleve_justifie()
    {
        Result<ClientDueDiligence> tooLow = ClientDueDiligence.Create(Draft(RiskLevel.Standard, RiskFactors.PoliticallyExposed), Reviewer, Today, Now);
        Result<ClientDueDiligence> unexplained = ClientDueDiligence.Create(Draft(RiskLevel.High, RiskFactors.HighRiskCountry, "  "), Reviewer, Today, Now);
        Result<ClientDueDiligence> lowUnexplained = ClientDueDiligence.Create(Draft(RiskLevel.Low), Reviewer, Today, Now);
        Result<ClientDueDiligence> high = ClientDueDiligence.Create(
            Draft(RiskLevel.High, RiskFactors.PoliticallyExposed | RiskFactors.CashIntensive, "  Dirigeant élu local.  "), Reviewer, Today, Now);

        tooLow.Errors.Should().ContainSingle().Which.Code.Should().Be(VigilanceErrors.LevelTooLowCode);
        unexplained.Errors.Should().ContainSingle().Which.Code.Should().Be(VigilanceErrors.JustificationRequiredCode);
        lowUnexplained.Errors.Should().ContainSingle().Which.Code.Should().Be(VigilanceErrors.JustificationRequiredCode);
        high.Value.Justification.Should().Be("Dirigeant élu local.");
        high.Value.NextReviewOn.Should().Be(new DateOnly(2027, 10, 1));
        ClientDueDiligence.Create(Draft(), Reviewer, Today, Now).Value.Justification.Should().BeNull("facultative pour un risque standard");
    }

    [Fact]
    public void Une_fiche_incoherente_est_refusee_point_par_point()
    {
        Result<ClientDueDiligence> refused = ClientDueDiligence.Create(
            new ClientDueDiligence.Draft((IdentityEvidence)64, Today.AddDays(1), [new(" ", 10m, OwnershipKind.Capital)], (RiskLevel)9, (RiskFactors)128,
                new string('a', ClientDueDiligence.MaxJustificationLength + 1), Today, null),
            Reviewer, Today, Now);

        refused.Errors.Select(e => e.Code).Should().BeEquivalentTo(
            VigilanceErrors.EvidenceInvalidCode, VigilanceErrors.DateInFutureCode, VigilanceErrors.OwnerNameInvalidCode, VigilanceErrors.OwnerShareInvalidCode,
            VigilanceErrors.LevelInvalidCode, VigilanceErrors.FactorsInvalidCode, VigilanceErrors.JustificationTooLongCode);
    }

    [Theory]
    [InlineData(OwnershipKind.Capital, null, VigilanceErrors.OwnerShareInvalidCode)]
    [InlineData(OwnershipKind.Capital, 25.0, VigilanceErrors.OwnerShareInvalidCode)]
    [InlineData(OwnershipKind.VotingRights, 100.5, VigilanceErrors.OwnerShareInvalidCode)]
    [InlineData(OwnershipKind.Control, 30.0, VigilanceErrors.OwnerShareInvalidCode)]
    [InlineData((OwnershipKind)9, null, VigilanceErrors.OwnerShareInvalidCode)]
    [InlineData(OwnershipKind.Capital, 50.0, VigilanceErrors.SharesOver100Code)]
    public void Les_parts_des_beneficiaires_respectent_le_seuil_de_25_pour_cent(OwnershipKind kind, double? share, string expected)
    {
        ClientDueDiligence.Draft draft = Draft() with
        {
            Owners = [new("Claire Martin", 60m, OwnershipKind.Capital), new("Paul Martin", (decimal?)share, kind)],
        };

        ClientDueDiligence.Create(draft, Reviewer, Today, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(expected);
    }

    [Fact]
    public void Un_representant_legal_ou_un_controle_n_a_pas_de_part_et_dix_beneficiaires_au_plus()
    {
        ClientDueDiligence.Draft representative = Draft() with
        {
            Owners = [new("Claire Martin", null, OwnershipKind.LegalRepresentative), new("Holding Martin", null, OwnershipKind.Control)],
        };
        ClientDueDiligence.Draft crowd = Draft() with { Owners = [.. Enumerable.Range(1, 11).Select(i => new ClientDueDiligence.OwnerDraft($"Associé {i}", null, OwnershipKind.Control))] };

        ClientDueDiligence.Create(representative, Reviewer, Today, Now).Value.Owners.Should().OnlyContain(o => o.SharePercent == null);
        ClientDueDiligence.Create(crowd, Reviewer, Today, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(VigilanceErrors.TooManyOwnersCode);
    }

    [Fact]
    public void La_revue_remplace_la_fiche_et_garde_l_ancienne_si_elle_est_refusee()
    {
        ClientDueDiligence file = ClientDueDiligence.Create(Draft(), Reviewer, Today, Now).Value;
        Guid colleague = Guid.CreateVersion7();

        file.Update(Draft(RiskLevel.Low), colleague, Today, Now.AddDays(1)).IsFailure.Should().BeTrue();
        file.Level.Should().Be(RiskLevel.Standard);
        file.ReviewedBy.Should().Be(Reviewer);
        file.Update(Draft(RiskLevel.Low, justification: "Client connu depuis dix ans.") with { Owners = [new("Claire Martin", 100m, OwnershipKind.Capital)] }, colleague, Today, Now.AddDays(1))
            .IsSuccess.Should().BeTrue();
        file.Owners.Should().ContainSingle().Which.SharePercent.Should().Be(100m);
        file.ReviewedBy.Should().Be(colleague);
        file.NextReviewOn.Should().Be(new DateOnly(2029, 10, 1));
        file.UpdatedAt.Should().Be(Now.AddDays(1));
    }
}
