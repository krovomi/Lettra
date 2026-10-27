using Lettra.Calculations.Compliance;
using Lettra.Domain.Billing;

namespace Lettra.Domain.Tests.Billing;

/// <summary>
/// Le calendrier de la réforme (décret 2024-266) : réception pour toutes les entreprises au 1er septembre 2026 ; émission
/// au 1er septembre 2026 pour les grandes entreprises et les ETI, au 1er septembre 2027 pour les PME et les
/// microentreprises. Les nouvelles mentions deviennent exigées à la date d'émission de l'entreprise.
/// </summary>
public sealed class EInvoicingCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CompanySize.Large, 2026)]
    [InlineData(CompanySize.Intermediate, 2026)]
    [InlineData(CompanySize.SmallOrMedium, 2027)]
    [InlineData(CompanySize.Micro, 2027)]
    [InlineData(CompanySize.Unknown, 2026)]
    public void La_date_d_emission_depend_de_la_categorie_d_entreprise(CompanySize size, int year)
    {
        EInvoicingCalendar.IssuingFrom(size).Should().Be(new DateOnly(year, 9, 1));
        EInvoicingCalendar.ReceivingFrom.Should().Be(new DateOnly(2026, 9, 1), "toutes les entreprises reçoivent dès la première vague");
    }

    [Fact]
    public void Une_pme_n_a_qu_un_avertissement_sur_les_mentions_de_la_reforme_avant_septembre_2027()
    {
        ComplianceDocument document = Document(EInvoicingCalendar.IssuingFrom(CompanySize.SmallOrMedium));

        Severity(document, new DateOnly(2026, 10, 15)).Should().Be(ComplianceSeverity.Warning);
        Severity(document, new DateOnly(2027, 9, 1)).Should().Be(ComplianceSeverity.Error);
    }

    [Fact]
    public void Une_grande_entreprise_doit_les_mentions_des_septembre_2026()
    {
        ComplianceDocument document = Document(EInvoicingCalendar.IssuingFrom(CompanySize.Large));

        Severity(document, new DateOnly(2026, 8, 31)).Should().Be(ComplianceSeverity.Warning);
        Severity(document, new DateOnly(2026, 9, 1)).Should().Be(ComplianceSeverity.Error);
        Severity(document with { ReformMentionsFrom = null }, new DateOnly(2026, 9, 1)).Should().Be(ComplianceSeverity.Error, "sans catégorie, la première vague s'applique");
    }

    [Fact]
    public void La_categorie_de_la_fiche_entreprise_donne_la_date_des_mentions()
    {
        CompanyProfile.Draft draft = new(
            "Conseil Lettra", "SAS", null, "552100554", null, null, "FR96552100554", "10 avenue de l'Opéra", null, "75001", "Paris", "FR",
            VatFranchise: false, VatOnDebits: false, PaymentDays: 30, null, null, null, null, null, Size: CompanySize.Micro);
        CompanyProfile profile = CompanyProfile.Create(draft, Now).Value;

        profile.Size.Should().Be(CompanySize.Micro);
        profile.ReformMentionsFrom.Should().Be(new DateOnly(2027, 9, 1));
        profile.ToSellerTerms().ReformMentionsFrom.Should().Be(new DateOnly(2027, 9, 1));

        profile.Update(draft with { Size = (CompanySize)9 }, Now)
            .Errors.Should().ContainSingle().Which.Target.Should().Be(nameof(CompanyProfile.Size));
    }

    private static ComplianceSeverity Severity(ComplianceDocument document, DateOnly today) =>
        ComplianceChecker.Check(document, today).Findings.Single(f => f.Code == MentionCodes.CustomerSiren).Severity;

    private static ComplianceDocument Document(DateOnly reformMentionsFrom) => new(
        CompliancePieceKind.Invoice,
        IsDraft: true,
        new ComplianceParty("Conseil Lettra", "10 avenue de l'Opéra 75001 Paris", "552100554", null, "FR96552100554", "FR", "SAS", 10_000m, "RCS Paris 552 100 554"),
        new ComplianceParty("Atelier Durand", "3 rue des Lilas 69001 Lyon", Siren: null, Siret: null, VatNumber: null, "FR"),
        SellerVatFranchise: false,
        SellerVatOnDebits: false,
        Number: null,
        IssueDate: null,
        DeliveryDate: null,
        DeliveryAddress: null,
        OperationCategory: "Services",
        DueDate: new DateOnly(2026, 11, 14),
        PaymentTerms: "Virement à 30 jours",
        LatePenaltyRate: 10m,
        EarlyPaymentDiscount: "Pas d'escompte",
        Lines: [new ComplianceLine("Conseil", 1m, 100m)],
        LegalMentions: [],
        ReformMentionsFrom: reformMentionsFrom);
}
