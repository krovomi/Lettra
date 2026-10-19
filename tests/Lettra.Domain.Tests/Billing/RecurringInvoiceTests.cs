using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Billing;

/// <summary>
/// Factures récurrentes : échéancier sans dérive de fin de mois, à échoir ou échu, date de fin, révision annuelle (fixe
/// ou Syntec), échéancier figé après la première facture, contrôles de saisie et facture créée avec sa période.
/// </summary>
public sealed class RecurringInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly CustomerId Client = new(Guid.CreateVersion7());

    private static readonly PartySnapshot Martin = new(
        "Boulangerie Martin", "1 rue de la Paix", null, "75002", "Paris", "FR", "732829320", "73282932000074", "FR44732829320",
        null, null, null, IsBusiness: true);

    private static LineDraft Service(decimal price) =>
        new(null, null, "Tenue comptable", null, CatalogItemKind.Service, "C62", 1m, price, 0m, "FR-20", null);

    private static RecurringInvoiceDraft Draft(
        RecurrenceFrequency frequency = RecurrenceFrequency.Monthly,
        BillingTiming timing = BillingTiming.InAdvance,
        DateOnly? start = null,
        DateOnly? end = null,
        PriceRevisionKind revision = PriceRevisionKind.None,
        decimal? percent = null,
        decimal? reference = null,
        decimal? latest = null,
        string? subject = "Honoraires",
        decimal price = 100m) =>
        new("Tenue mensuelle", Client, VatRegime.Standard, subject, "Merci", "Virement à 30 jours", [Service(price)], frequency, timing,
            start ?? new DateOnly(2026, 1, 31), end, ValidateAutomatically: false, revision, percent, reference, latest);

    private static RecurringInvoice Template(RecurringInvoiceDraft draft) =>
        RecurringInvoice.Create(RecurringInvoiceId.New(), draft, User, Now).Value;

    private static Invoice Generate(RecurringInvoice template, DateOnly today) =>
        template.GenerateNext(new InvoiceId(Guid.CreateVersion7()), Martin, today, User, Now).Value;

    [Fact]
    public void Les_periodes_mensuelles_partent_toujours_du_jour_de_debut_sans_deriver_en_fin_de_mois()
    {
        RecurringInvoice template = Template(Draft());

        template.PeriodAt(0).Should().Be(new BillingPeriod(new(2026, 1, 31), new(2026, 2, 27), new(2026, 1, 31)));
        template.PeriodAt(1).Should().Be(new BillingPeriod(new(2026, 2, 28), new(2026, 3, 30), new(2026, 2, 28)));
        template.PeriodAt(2)!.Start.Should().Be(new DateOnly(2026, 3, 31), "le 31 revient dès que le mois le permet");
        template.NextIssueDate.Should().Be(new DateOnly(2026, 1, 31));
        template.Version.Should().Be(1);
        template.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Quarterly, "2026-03-31")]
    [InlineData(RecurrenceFrequency.Yearly, "2026-12-31")]
    public void Une_facturation_echue_est_due_le_dernier_jour_de_la_periode(RecurrenceFrequency frequency, string lastDay)
    {
        RecurringInvoice template = Template(Draft(frequency, BillingTiming.InArrears, new DateOnly(2026, 1, 1)));

        template.NextPeriod!.End.Should().Be(DateOnly.Parse(lastDay, System.Globalization.CultureInfo.InvariantCulture));
        template.NextPeriod.IssueOn.Should().Be(template.NextPeriod.End);
        template.DuePeriod(template.NextPeriod.End.AddDays(-1)).Should().BeNull();
        template.DuePeriod(template.NextPeriod.End).Should().NotBeNull();
    }

    [Fact]
    public void La_facture_creee_porte_la_periode_l_objet_complete_et_les_conditions_du_modele()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 10, 1)));
        long version = template.Version;

        Invoice invoice = Generate(template, new DateOnly(2026, 10, 19));

        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.Number.Should().BeNull("le numéro reste attribué à la validation");
        invoice.BillingPeriodStart.Should().Be(new DateOnly(2026, 10, 1));
        invoice.BillingPeriodEnd.Should().Be(new DateOnly(2026, 10, 31));
        invoice.Subject.Should().Be("Honoraires (du 01/10/2026 au 31/10/2026)");
        invoice.PaymentTerms.Should().Be("Virement à 30 jours");
        invoice.Notes.Should().Be("Merci");
        invoice.CustomerId.Should().Be(Client);
        invoice.Lines.Should().ContainSingle().Which.UnitPriceExclTax.Should().Be(100m);

        template.GeneratedCount.Should().Be(1);
        template.Occurrences.Should().ContainSingle().Which.InvoiceId.Should().Be(invoice.Id);
        template.NextIssueDate.Should().Be(new DateOnly(2026, 11, 1));
        template.Version.Should().Be(version + 1);
    }

    [Fact]
    public void Sans_objet_la_periode_devient_l_objet_et_rien_n_est_cree_avant_l_echeance()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 11, 1), subject: null));

        template.GenerateNext(new InvoiceId(Guid.CreateVersion7()), Martin, new DateOnly(2026, 10, 31), User, Now)
            .Errors.Single().Code.Should().Be(RecurringInvoiceErrors.NothingDueCode);
        Generate(template, new DateOnly(2026, 11, 1)).Subject.Should().Be("Période du 01/11/2026 au 30/11/2026");
    }

    [Fact]
    public void La_date_de_fin_arrete_les_periodes_suivantes()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 2, 15)));

        Generate(template, new DateOnly(2026, 12, 31));
        Generate(template, new DateOnly(2026, 12, 31));

        template.NextPeriod.Should().BeNull();
        template.NextIssueDate.Should().BeNull();
        template.DuePeriod(new DateOnly(2027, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Un_modele_en_pause_n_a_plus_d_echeance_jusqu_a_sa_reprise()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 1, 1)));
        long version = template.Version;

        template.SetActive(false, User, Now);
        template.SetActive(false, User, Now);

        template.NextIssueDate.Should().BeNull();
        template.DuePeriod(new DateOnly(2026, 6, 1)).Should().BeNull();
        template.Version.Should().Be(version + 1, "une pause répétée ne change rien");

        template.SetActive(true, User, Now);
        template.NextIssueDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void La_revision_fixe_s_applique_a_chaque_anniversaire_et_se_note_sur_la_periode()
    {
        RecurringInvoice template = Template(Draft(RecurrenceFrequency.Yearly, start: new DateOnly(2025, 1, 1),
            revision: PriceRevisionKind.FixedPercent, percent: 3m, price: 1000m));
        template.NextRevisionDate.Should().Be(new DateOnly(2026, 1, 1));

        Generate(template, new DateOnly(2027, 6, 1)).Lines[0].UnitPriceExclTax.Should().Be(1000m);
        Invoice second = Generate(template, new DateOnly(2027, 6, 1));
        Invoice third = Generate(template, new DateOnly(2027, 6, 1));

        second.Lines[0].UnitPriceExclTax.Should().Be(1030m);
        third.Lines[0].UnitPriceExclTax.Should().Be(1060.90m);
        template.Occurrences.Select(o => o.RevisionCoefficient).Should().Equal(null, 1.03m, 1.03m);
        template.NextRevisionDate.Should().Be(new DateOnly(2028, 1, 1));
        template.TotalExclTax.Should().Be(1060.90m);
    }

    [Fact]
    public void La_revision_Syntec_attend_le_nouvel_indice_puis_s_applique_une_fois()
    {
        RecurringInvoice template = Template(Draft(RecurrenceFrequency.Yearly, start: new DateOnly(2025, 1, 1),
            revision: PriceRevisionKind.SyntecIndex, reference: 300m, price: 1000m));
        Generate(template, new DateOnly(2026, 1, 1));

        template.RevisionAwaitsIndex.Should().BeTrue();
        Generate(template, new DateOnly(2026, 1, 1)).Lines[0].UnitPriceExclTax.Should().Be(1000m, "sans indice, les prix restent");
        template.RevisionAwaitsIndex.Should().BeTrue("l'anniversaire 2026 attend toujours son indice");

        RecurringInvoiceDraft withIndex = Draft(RecurrenceFrequency.Yearly, start: new DateOnly(2025, 1, 1),
            revision: PriceRevisionKind.SyntecIndex, reference: 300m, latest: 309m, price: 1000m);
        template.Update(withIndex, User, Now).IsSuccess.Should().BeTrue();
        Invoice revised = Generate(template, new DateOnly(2027, 1, 1));

        revised.Lines[0].UnitPriceExclTax.Should().Be(1030m);
        template.Occurrences[^1].RevisionCoefficient.Should().Be(1.03m);
        template.SyntecReferenceIndex.Should().Be(309m, "le nouvel indice devient la référence");
        template.NextRevisionDate.Should().Be(new DateOnly(2028, 1, 1));
    }

    [Fact]
    public void Une_revision_choisie_en_cours_de_contrat_ne_rattrape_pas_le_passe()
    {
        RecurringInvoice template = Template(Draft(RecurrenceFrequency.Yearly, start: new DateOnly(2024, 1, 1), price: 1000m));
        Generate(template, new DateOnly(2026, 6, 1));
        Generate(template, new DateOnly(2026, 6, 1));

        template.Update(Draft(RecurrenceFrequency.Yearly, start: new DateOnly(2024, 1, 1), revision: PriceRevisionKind.FixedPercent, percent: 2m, price: 1000m), User, Now)
            .IsSuccess.Should().BeTrue();

        template.NextRevisionDate.Should().Be(new DateOnly(2026, 1, 1));
        Generate(template, new DateOnly(2026, 6, 1)).Lines[0].UnitPriceExclTax.Should().Be(1020m);
    }

    [Fact]
    public void Apres_une_premiere_facture_le_debut_et_la_frequence_sont_figes_mais_les_lignes_changent()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 1, 1)));
        Generate(template, new DateOnly(2026, 1, 1));

        template.Update(Draft(start: new DateOnly(2026, 2, 1)), User, Now).Errors.Single().Code.Should().Be(RecurringInvoiceErrors.ScheduleLockedCode);
        template.Update(Draft(RecurrenceFrequency.Quarterly, start: new DateOnly(2026, 1, 1)), User, Now)
            .Errors.Single().Code.Should().Be(RecurringInvoiceErrors.ScheduleLockedCode);
        template.Update(Draft(start: new DateOnly(2026, 1, 1), price: 150m), User, Now).IsSuccess.Should().BeTrue();

        template.TotalExclTax.Should().Be(150m);
        Generate(template, new DateOnly(2026, 2, 1)).Lines[0].UnitPriceExclTax.Should().Be(150m);
    }

    [Fact]
    public void Une_saisie_invalide_renvoie_toutes_ses_erreurs()
    {
        RecurringInvoiceDraft invalid = new(" ", new CustomerId(Guid.Empty), (VatRegime)99, null, null, null, [], (RecurrenceFrequency)9, (BillingTiming)9,
            new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), false, PriceRevisionKind.FixedPercent, 0m, null, null);

        RecurringInvoice.Create(RecurringInvoiceId.New(), invalid, User, Now).Errors.Select(e => e.Code).Should().Contain(
        [
            RecurringInvoiceErrors.NameRequiredCode, BillingErrors.CustomerRequiredCode, BillingErrors.VatRegimeInvalidCode,
            RecurringInvoiceErrors.FrequencyInvalidCode, RecurringInvoiceErrors.TimingInvalidCode, RecurringInvoiceErrors.EndBeforeStartCode,
            RecurringInvoiceErrors.RevisionPercentInvalidCode, BillingErrors.LinesRequiredCode,
        ]);
    }

    [Fact]
    public void Les_indices_Syntec_et_le_mode_de_revision_sont_controles()
    {
        RecurringInvoice.Create(RecurringInvoiceId.New(), Draft(revision: PriceRevisionKind.SyntecIndex, reference: null, latest: -1m), User, Now)
            .Errors.Select(e => e.Target).Should().Equal("SyntecReferenceIndex", "SyntecLatestIndex");
        RecurringInvoice.Create(RecurringInvoiceId.New(), Draft(revision: (PriceRevisionKind)7), User, Now)
            .Errors.Single().Code.Should().Be(RecurringInvoiceErrors.RevisionInvalidCode);
        RecurringInvoice.Create(RecurringInvoiceId.New(), Draft() with { Name = new string('a', RecurringInvoice.MaxNameLength + 1) }, User, Now)
            .Errors.Single().Code.Should().Be(RecurringInvoiceErrors.NameTooLongCode);
    }

    [Fact]
    public void Un_objet_long_est_raccourci_mais_garde_la_periode_entiere()
    {
        RecurringInvoice template = Template(Draft(start: new DateOnly(2026, 1, 1), subject: new string('x', 195)));

        string subject = Generate(template, new DateOnly(2026, 1, 1)).Subject!;

        subject.Should().HaveLength(200).And.EndWith("(du 01/01/2026 au 31/01/2026)");
    }

    [Fact]
    public void Une_periode_de_facturation_inversee_est_refusee()
    {
        Action inverted = () => Invoice.CreateForPeriod(new InvoiceId(Guid.CreateVersion7()), Client, Martin,
            new InvoiceHeader(null, null, null, null, null, VatRegime.Standard, null, null), [Service(10m)], new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), User, Now);

        inverted.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(100, 1.025, 102.50)]
    [InlineData(10.01, 1.03, 10.31)]
    [InlineData(0.05, 1.1, 0.06)]
    public void Un_prix_revise_est_arrondi_au_centime_demi_au_dessus(decimal price, decimal coefficient, decimal expected) =>
        PriceRevision.Apply(price, coefficient).Should().Be(expected);
}
