using Lettra.Domain.Banking;

namespace Lettra.Domain.Tests.Banking;

/// <summary>Codes de lettrage et regroupement des écritures soldées d'un client.</summary>
public sealed class LetteringTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Les_codes_suivent_l_ordre_des_logiciels_comptables(int index, string code) =>
        LetteringCodes.FromIndex(index).Should().Be(code);

    [Fact]
    public void Une_facture_soldee_par_deux_reglements_se_lettre_avec_les_deux()
    {
        Guid invoice = Guid.CreateVersion7();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        var plan = LetteringPlanner.Plan(second,
            [new(invoice, null, Day, 0m, false)],
            [new(first, Day.AddDays(3), [invoice]), new(second, Day.AddDays(9), [invoice])],
            new HashSet<LetteringItem>());

        plan.Should().NotBeNull();
        plan!.Value.Items.Should().HaveCount(3).And.Contain(new LetteringItem(LetteredEntryKind.Payment, first));
        plan.Value.Date.Should().Be(Day.AddDays(9));
    }

    [Fact]
    public void Une_facture_encore_due_ne_se_lettre_pas()
    {
        Guid invoice = Guid.CreateVersion7();
        Guid payment = Guid.CreateVersion7();

        LetteringPlanner.Plan(payment, [new(invoice, null, Day, 50m, false)], [new(payment, Day, [invoice])], new HashSet<LetteringItem>())
            .Should().BeNull();
    }

    [Fact]
    public void L_avoir_d_une_facture_entre_dans_le_groupe()
    {
        Guid invoice = Guid.CreateVersion7();
        Guid credit = Guid.CreateVersion7();
        Guid payment = Guid.CreateVersion7();

        var plan = LetteringPlanner.Plan(payment,
            [new(invoice, null, Day, 0m, false), new(credit, invoice, Day.AddDays(1), 0m, true)],
            [new(payment, Day.AddDays(2), [invoice])],
            new HashSet<LetteringItem>());

        plan!.Value.Items.Should().Contain(new LetteringItem(LetteredEntryKind.Invoice, credit));
    }

    [Fact]
    public void Une_ecriture_deja_lettree_bloque_un_second_lettrage()
    {
        Guid invoice = Guid.CreateVersion7();
        Guid payment = Guid.CreateVersion7();

        LetteringPlanner.Plan(payment, [new(invoice, null, Day, 0m, false)], [new(payment, Day, [invoice])],
            new HashSet<LetteringItem> { new(LetteredEntryKind.Invoice, invoice) }).Should().BeNull();
    }
}
