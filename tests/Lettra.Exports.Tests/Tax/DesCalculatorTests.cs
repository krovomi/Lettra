using System.Text;
using Lettra.Calculations;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;
using static Lettra.Exports.Tests.Tax.TaxSamples;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Déclaration européenne de services : qui y figure, une ligne par numéro de TVA, contrôles, fichier.</summary>
public sealed class DesCalculatorTests
{
    private static readonly DateOnly Due = FiscalCalendar.DesDueDate(2026, 9);

    private static TaxDocument IntraEu(string number, decimal amount, PartySnapshot customer, Guid? customerId = null, InvoiceKind kind = InvoiceKind.Invoice) =>
        Invoice(number, new(2026, 9, 10), [Services(amount, 0m, VatCategoryCodes.ReverseCharge, "VATEX-EU-AE")], customer, VatRegime.ReverseChargeIntraEu, kind, customerId: customerId);

    [Fact]
    public void Une_ligne_par_numero_de_tva_avec_le_montant_du_mois_arrondi_a_l_euro()
    {
        PartySnapshot berlin = Customer("Berlin Beratung GmbH", "DE", "de 123 456 789", siren: null);
        List<VatOperation> operations =
        [
            .. VatOperations.Of(IntraEu("FAC-000101", 1200.40m, berlin, Berlin)),
            .. VatOperations.Of(IntraEu("FAC-000102", 300.20m, berlin, Berlin)),
            .. VatOperations.Of(IntraEu("AV-000101", 100m, berlin, Berlin, InvoiceKind.CreditNote)),
            .. VatOperations.Of(Invoice("FAC-000103", new(2026, 9, 12), [Services(500m)])),
            .. VatOperations.Of(Invoice("FAC-000104", new(2026, 9, 12), [Goods(500m, 0m, VatCategoryCodes.IntraCommunity)], berlin, VatRegime.IntraEuGoodsSupply)),
        ];

        DesResponse des = DesCalculator.Build(2026, 9, Due, operations, null);

        DesLineResponse line = des.Lines.Should().ContainSingle().Subject;
        line.CustomerVatNumber.Should().Be("DE123456789");
        line.CountryCode.Should().Be("DE");
        line.Amount.Should().Be(1401m, "1 200,40 + 300,20 − 100 = 1 400,60, arrondi à l'euro");
        line.Sources.Select(s => s.PieceNumber).Should().BeEquivalentTo(["FAC-000101", "FAC-000102", "AV-000101"], "les biens et les clients français n'y vont pas");
        des.Total.Should().Be(1401m);
        des.Checks.Should().ContainSingle().Which.Severity.Should().Be(TaxCheckSeverityDto.Ok);
    }

    [Fact]
    public void Un_client_sans_numero_de_tva_bloque_et_un_client_hors_union_avertit()
    {
        List<VatOperation> operations =
        [
            .. VatOperations.Of(IntraEu("FAC-000201", 100m, Customer("Madrid SL", "ES", vatNumber: null, siren: null))),
            .. VatOperations.Of(IntraEu("FAC-000202", 100m, Customer("Zurich AG", "CH", "CHE123456789", siren: null))),
        ];

        DesResponse des = DesCalculator.Build(2026, 9, Due, operations, null);

        des.Lines.Should().BeEmpty();
        des.Checks.Should().Contain(c => c.Code == DesCalculator.CheckVatNumberMissing && c.Severity == TaxCheckSeverityDto.Blocking && c.Pieces!.Contains("FAC-000201"));
        des.Checks.Should().Contain(c => c.Code == DesCalculator.CheckNotEu && c.Severity == TaxCheckSeverityDto.Warning && c.Pieces!.Contains("FAC-000202"));
    }

    [Fact]
    public void Un_avoir_plus_fort_que_les_factures_du_mois_avertit()
    {
        PartySnapshot berlin = Customer("Berlin Beratung GmbH", "DE", "DE123456789", siren: null);

        DesResponse des = DesCalculator.Build(2026, 9, Due, [.. VatOperations.Of(IntraEu("AV-000301", 100m, berlin, kind: InvoiceKind.CreditNote))], null);

        des.Lines.Should().ContainSingle().Which.Amount.Should().Be(-100m);
        des.Checks.Should().Contain(c => c.Code == DesCalculator.CheckNegative);
    }

    [Fact]
    public void Le_fichier_a_une_ligne_par_client_et_la_periode()
    {
        PartySnapshot berlin = Customer("Berlin Beratung GmbH", "DE", "DE123456789", siren: null);
        DesResponse des = DesCalculator.Build(2026, 9, Due, [.. VatOperations.Of(IntraEu("FAC-000401", 999.5m, berlin))], null);

        string csv = Encoding.UTF8.GetString(DesCalculator.ToCsv(des));

        csv.Should().Be("ligne;numero_tva_client;montant_eur;periode\r\n1;DE123456789;1000;202609\r\n");
    }

    [Fact]
    public void La_des_se_depose_le_dixieme_jour_ouvrable_du_mois_suivant()
    {
        // Octobre 2026 : le 1er est un jeudi, les dimanches 4 et 11 ne comptent pas, le 10e jour ouvrable est le 12.
        FiscalCalendar.DesDueDate(2026, 9).Should().Be(new DateOnly(2026, 10, 12));
        // Mai 2026 : 1er (fête du travail), 8 (victoire 1945) et 14 (Ascension) ne comptent pas : le 10e jour ouvrable est le 15.
        FiscalCalendar.DesDueDate(2026, 4).Should().Be(new DateOnly(2026, 5, 15));
    }

    [Fact]
    public void La_DES_vise_les_26_autres_etats_membres() =>
        DesCalculator.OtherMemberStates.Should().HaveCount(26)
            .And.BeEquivalentTo(Lettra.Calculations.EuropeanUnion.MemberStates.Where(c => c != "FR"));
}
