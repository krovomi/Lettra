using Lettra.Contracts.Customers;
using Lettra.UI.Shared.Billing;

namespace Lettra.UI.Tests.Billing;

public sealed class CustomerDirectoryTests
{
    private static readonly Guid Dupont = Guid.NewGuid();
    private static readonly Guid DupontFreres = Guid.NewGuid();

    private static FakeCustomersGateway Gateway()
    {
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(Dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        customers.Customers.Add(new CustomerSummary(DupontFreres, CustomerKindDto.Company, "Dupont Frères Distribution", "552100554", "Nantes"));
        customers.Customers.Add(new CustomerSummary(Guid.NewGuid(), CustomerKindDto.Company, "Martin Conseil", "443061841", "Paris"));
        return customers;
    }

    [Fact]
    public async Task Le_nom_dit_trouve_le_client_le_plus_proche_par_une_seule_recherche()
    {
        FakeCustomersGateway customers = Gateway();

        Guid? found = await customers.FindBestMatchAsync("dupont", CancellationToken.None);

        found.Should().Be(Dupont, "le mot commence le nom, et le nom court l'emporte");
        customers.Searches.Should().Equal("dupont");
    }

    [Fact]
    public async Task La_recherche_part_du_mot_le_plus_long_et_tous_les_mots_doivent_correspondre()
    {
        FakeCustomersGateway customers = Gateway();

        (await customers.FindBestMatchAsync("Dupont distribution", CancellationToken.None)).Should().Be(DupontFreres);
        customers.Searches.Should().Equal("distribution");
        (await customers.FindBestMatchAsync("Dupont Paris", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" - ")]
    public async Task Un_nom_vide_ne_cherche_rien(string name)
    {
        FakeCustomersGateway customers = Gateway();

        (await customers.FindBestMatchAsync(name, CancellationToken.None)).Should().BeNull();
        customers.Searches.Should().BeEmpty();
    }
}
