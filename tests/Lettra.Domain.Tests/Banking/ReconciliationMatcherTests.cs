using Lettra.Domain.Banking;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Banking;

/// <summary>Propositions de rapprochement : déterministes, dans l'ordre des règles, jamais ambiguës.</summary>
public sealed class ReconciliationMatcherTests
{
    private static readonly CustomerId Dupont = new(Guid.CreateVersion7());
    private static readonly CustomerId Martin = new(Guid.CreateVersion7());

    private static OpenInvoice Invoice(string number, CustomerId customer, string name, decimal outstanding, int dueDay = 1) =>
        new(Guid.CreateVersion7(), number, customer, name, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, dueDay), outstanding);

    private static BankLineCandidate Line(decimal amount, string label, string? counterparty = null, string? ibanHash = null) =>
        new(Guid.CreateVersion7(), new DateOnly(2026, 10, 12), amount, label, null, counterparty, ibanHash);

    [Fact]
    public void Le_numero_de_facture_dans_le_libelle_l_emporte_meme_mal_espace()
    {
        OpenInvoice invoice = Invoice("FAC-2026-000142", Dupont, "Dupont SARL", 1200m);
        OpenInvoice other = Invoice("FAC-2026-000143", Martin, "Boulangerie Martin", 1200m);

        MatchProposal? proposal = ReconciliationMatcher.Propose(Line(1200m, "VIR SEPA BOULANGERIE MARTIN FAC 2026 000142"), [invoice, other], [], 1m);

        proposal.Should().NotBeNull();
        proposal!.Rule.Should().Be(MatchRule.InvoiceNumber);
        proposal.CustomerId.Should().Be(Dupont);
        proposal.Allocations.Should().ContainSingle().Which.Amount.Should().Be(1200m);
        proposal.Evidence.Should().Be("FAC-2026-000142");
    }

    [Fact]
    public void Un_numero_plus_long_ne_correspond_pas() =>
        ReconciliationMatcher.Propose(Line(50m, "VIR FAC-2026-0001420"), [Invoice("FAC-2026-000142", Dupont, "Dupont", 70m)], [], 1m).Should().BeNull();

    [Fact]
    public void Un_paiement_partiel_par_numero_laisse_la_facture_due_et_un_petit_manque_devient_un_ecart()
    {
        OpenInvoice invoice = Invoice("F2026-0007", Dupont, "Dupont SARL", 1000m);

        ReconciliationMatcher.Propose(Line(500m, "F2026-0007 acompte"), [invoice], [], 1m)!.Allocations.Single().Amount.Should().Be(500m);

        MatchProposal shortPaid = ReconciliationMatcher.Propose(Line(999.60m, "F2026-0007"), [invoice], [], 1m)!;
        shortPaid.Allocations.Single().Amount.Should().Be(1000m);
        shortPaid.WriteOff.Should().Be(0.40m);
    }

    [Fact]
    public void Montant_exact_et_nom_du_client_sans_les_formes_juridiques()
    {
        OpenInvoice invoice = Invoice("F1", Martin, "SARL Boulangerie Martin", 80m);

        MatchProposal? proposal = ReconciliationMatcher.Propose(Line(80m, "VIR BOULANGERIE MARTIN OCTOBRE"), [invoice, Invoice("F2", Dupont, "Dupont SAS", 80m)], [], 1m);

        proposal!.Rule.Should().Be(MatchRule.AmountAndName);
        proposal.CustomerId.Should().Be(Martin);
    }

    [Fact]
    public void Montant_exact_et_iban_deja_connu()
    {
        OpenInvoice invoice = Invoice("F1", Dupont, "Dupont SARL", 300m);

        MatchProposal? proposal = ReconciliationMatcher.Propose(Line(300m, "VIREMENT RECU", ibanHash: "h1"), [invoice], [new KnownIban(Dupont, "h1")], 1m);

        proposal!.Rule.Should().Be(MatchRule.AmountAndIban);
    }

    [Fact]
    public void La_somme_de_plusieurs_factures_d_un_client_reconnu()
    {
        OpenInvoice a = Invoice("F1", Dupont, "Dupont SARL", 100m, 1);
        OpenInvoice b = Invoice("F2", Dupont, "Dupont SARL", 250m, 2);
        OpenInvoice c = Invoice("F3", Dupont, "Dupont SARL", 75m, 3);

        MatchProposal? proposal = ReconciliationMatcher.Propose(Line(325m, "VIR DUPONT"), [a, b, c], [], 1m);

        proposal!.Rule.Should().Be(MatchRule.SumOfInvoices);
        proposal.Allocations.Select(x => x.Number).Should().Equal("F2", "F3");
        proposal.WriteOff.Should().Be(0m);
    }

    [Fact]
    public void Un_debit_ou_une_ambiguite_ne_propose_rien()
    {
        OpenInvoice dupont = Invoice("F1", Dupont, "Dupont", 80m);
        OpenInvoice martin = Invoice("F2", Martin, "Martin", 80m);

        ReconciliationMatcher.Propose(Line(-12.50m, "FRAIS F1"), [dupont], [], 1m).Should().BeNull();
        ReconciliationMatcher.Propose(Line(80m, "VIR DUPONT MARTIN"), [dupont, martin], [], 1m).Should().BeNull("deux clients possibles");
        ReconciliationMatcher.Propose(Line(80m, "VIREMENT"), [dupont, martin], [], 1m).Should().BeNull();
    }

    [Fact]
    public void Une_facture_proposee_ne_l_est_plus_pour_l_operation_suivante()
    {
        OpenInvoice invoice = Invoice("F2026-0001", Dupont, "Dupont", 100m);

        IReadOnlyList<MatchProposal> proposals = ReconciliationMatcher.ProposeAll(
            [Line(100m, "F2026-0001"), Line(100m, "F2026-0001 doublon")], [invoice], [], 1m);

        proposals.Should().ContainSingle();
    }
}
