using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Tri et bornes de montant des listes de devis et de factures : ce que la validation refuse.</summary>
public sealed class BillingListValidationTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    [Fact]
    public void Tri_et_bornes_valides_acceptes()
    {
        new ListInvoicesValidator().Validate(new ListInvoicesQuery(
            Dossier, Sort: BillingSortDto.TotalInclTax, Direction: SortDirectionDto.Ascending, MinAmount: 10m, MaxAmount: 10m)).Should().BeEmpty();
        new ListQuotesValidator().Validate(new ListQuotesQuery(Dossier, Sort: BillingSortDto.DueDate, MinAmount: -50m)).Should().BeEmpty();
    }

    [Fact]
    public void Borne_basse_au_dessus_de_la_borne_haute_refusee()
    {
        new ListInvoicesValidator().Validate(new ListInvoicesQuery(Dossier, MinAmount: 500m, MaxAmount: 100m))
            .Should().ContainSingle().Which.Target.Should().Be("MaxAmount");
        new ListQuotesValidator().Validate(new ListQuotesQuery(Dossier, MinAmount: 500m, MaxAmount: 100m))
            .Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Invalid);
    }

    [Fact]
    public void Colonne_ou_sens_inconnus_refuses()
    {
        new ListInvoicesValidator().Validate(new ListInvoicesQuery(Dossier, Sort: (BillingSortDto)99, Direction: 0))
            .Select(e => e.Target).Should().Equal("Sort", "Direction");
    }
}
