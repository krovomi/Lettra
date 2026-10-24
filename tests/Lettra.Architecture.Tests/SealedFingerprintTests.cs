using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Domain.Banking;

namespace Lettra.Architecture.Tests;

/// <summary>
/// Les empreintes scellées au journal d'intégrité écrivent certaines énumérations par leur nom : la forme canonique d'une
/// pièce (<see cref="CanonicalDocument"/>) et l'empreinte v1 d'un encaissement (<see cref="Payment.ComputeFingerprint"/>).
/// Renommer ou retirer un de ces membres changerait l'empreinte recalculée et ferait échouer le contrôle d'intégrité de
/// tous les journaux déjà scellés. Ajouter un membre reste possible.
/// </summary>
public sealed class SealedFingerprintTests
{
    [Fact]
    public void Les_noms_scelles_de_la_forme_canonique_ne_changent_jamais()
    {
        Enum.GetNames<InvoiceKindDto>().Should().Contain(["Invoice", "DownPayment", "CreditNote"]);
        Enum.GetNames<VatRegimeDto>().Should().Contain(
            ["Standard", "Franchise", "ReverseChargeIntraEu", "ReverseChargeConstruction", "IntraEuGoodsSupply", "Export"]);
        Enum.GetNames<VatCategoryDto>().Should().Contain(
            ["Standard", "ZeroRated", "Exempt", "ReverseCharge", "IntraCommunity", "Export", "OutOfScope"]);
        Enum.GetNames<OperationCategoryDto>().Should().Contain(["Goods", "Services", "Mixed"]);
        Enum.GetNames<CatalogItemKindDto>().Should().Contain(["Good", "Service"]);
    }

    [Fact]
    public void Les_noms_scelles_de_l_empreinte_d_encaissement_ne_changent_jamais()
    {
        Enum.GetNames<PaymentMethod>().Should().Contain(["Transfer", "Cheque", "Card", "Cash", "DirectDebit"]);
        Enum.GetNames<PaymentStatus>().Should().Contain(["Posted", "Reversed"]);
    }
}
