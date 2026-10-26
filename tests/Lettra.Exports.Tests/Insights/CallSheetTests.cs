using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// La fiche d'appel : les factures dues du client, les plus en retard d'abord, et les points à aborder dans l'ordre où
/// le collaborateur doit les dire (litige avant tout, puis douteuses, relances, indemnité, habitude, dernier paiement).
/// </summary>
public sealed class CallSheetTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid Customer = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    private static PaymentHistoryInvoice Invoice(string number, DateOnly due, decimal amount, decimal outstanding, Guid? customer = null, params PaymentHistoryEntry[] payments) =>
        new(Guid.CreateVersion7(), customer ?? Customer, "Boulangerie Martin", number, due.AddDays(-30), due, amount, outstanding, payments);

    private static PaymentReminder Reminder(PaymentHistoryInvoice invoice, ReminderLevel level, DateOnly on, ReminderChannel channel = ReminderChannel.Email) =>
        PaymentReminder.Record(
            new ReminderDraft(new InvoiceId(invoice.InvoiceId), new CustomerId(Customer), invoice.Number, level, "fr", "Relance", "Texte", invoice.Outstanding, 10),
            channel,
            channel == ReminderChannel.Email ? "compta@martin.fr" : null,
            errorCode: null,
            ReminderTrigger.User,
            Guid.CreateVersion7(),
            new DateTimeOffset(on.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            on);

    private static ReminderCustomer Professional => new(new CustomerId(Customer), "Boulangerie Martin", "compta@martin.fr", "fr", IsProfessional: true);

    [Fact]
    public void Les_points_viennent_dans_l_ordre_ou_il_faut_les_dire()
    {
        PaymentHistoryInvoice paid = Invoice("FA-1", new DateOnly(2026, 6, 1), 500m, 0m, null, new PaymentHistoryEntry(new DateOnly(2026, 6, 20), 500m));
        PaymentHistoryInvoice old = Invoice("FA-2", new DateOnly(2026, 7, 1), 1_200m, 1_200m);
        PaymentHistoryInvoice disputed = Invoice("FA-3", new DateOnly(2026, 10, 1), 800m, 800m);
        PaymentHistoryInvoice notDue = Invoice("FA-4", new DateOnly(2026, 11, 15), 300m, 300m);
        PaymentHistoryInvoice someoneElse = Invoice("FA-9", new DateOnly(2026, 7, 1), 9_999m, 9_999m, Other);
        CallSheetInput input = new(
            Professional,
            [paid, old, disputed, notDue, someoneElse],
            new Dictionary<Guid, PlatformStatusDto> { [disputed.InvoiceId] = PlatformStatusDto.Disputed },
            [Reminder(old, ReminderLevel.Friendly, new DateOnly(2026, 8, 1)), Reminder(old, ReminderLevel.Final, new DateOnly(2026, 9, 15))]);

        CallSheetResponse sheet = CallSheets.Build(Customer, input, Today)!;

        sheet.CustomerName.Should().Be("Boulangerie Martin");
        sheet.Email.Should().Be("compta@martin.fr");
        (sheet.OutstandingInclTax, sheet.OverdueInclTax).Should().Be((2_300m, 2_000m));
        sheet.Invoices.Select(i => (i.Number, i.DaysLate)).Should().Equal(("FA-2", 117), ("FA-3", 25), ("FA-4", 0));
        sheet.Invoices[0].Should().Match<CallSheetInvoiceResponse>(i => i.LastReminderLevel == ReminderLevelDto.Final && i.LastReminderOn == new DateOnly(2026, 9, 15));
        sheet.Invoices[1].PlatformStatus.Should().Be(PlatformStatusDto.Disputed);
        sheet.Reminders.Select(r => r.Level).Should().Equal(ReminderLevelDto.Final, ReminderLevelDto.Friendly);
        sheet.Points.Select(p => p.Kind).Should().Equal(
            CallPointKindDto.Disputed,
            CallPointKindDto.Doubtful,
            CallPointKindDto.FinalSent,
            CallPointKindDto.LastReminder,
            CallPointKindDto.Indemnity,
            CallPointKindDto.Habit,
            CallPointKindDto.LastPayment);
        sheet.Points[0].Should().Match<CallPointResponse>(p => p.Number == "FA-3" && p.Amount == 800m);
        sheet.Points[1].Should().Match<CallPointResponse>(p => p.Count == 1 && p.Days == 117 && p.Amount == 1_200m);
        sheet.Points[4].Amount.Should().Be(80m, "40 € par facture en retard, pour un client professionnel");
        sheet.Points[6].Should().Match<CallPointResponse>(p => p.Date == new DateOnly(2026, 6, 20) && p.Amount == 500m);
    }

    [Fact]
    public void Un_client_jamais_relance_le_dit_et_un_particulier_ne_doit_pas_d_indemnite()
    {
        PaymentHistoryInvoice late = Invoice("FA-5", new DateOnly(2026, 10, 10), 400m, 400m);
        CallSheetInput input = new(Professional with { IsProfessional = false }, [late], new Dictionary<Guid, PlatformStatusDto>(), []);

        CallSheetResponse sheet = CallSheets.Build(Customer, input, Today)!;

        sheet.Points.Select(p => p.Kind).Should().Equal(CallPointKindDto.NeverReminded);
        sheet.Points[0].Should().Match<CallPointResponse>(p => p.Count == 1 && p.Amount == 400m);
        sheet.Weather.Should().NotBeNull();
    }

    [Fact]
    public void Un_client_sans_facture_a_une_fiche_vide_et_un_client_inconnu_n_en_a_pas()
    {
        CallSheetInput known = new(Professional, [], new Dictionary<Guid, PlatformStatusDto>(), []);
        CallSheetInput unknown = known with { Customer = null };

        CallSheetResponse sheet = CallSheets.Build(Customer, known, Today)!;

        sheet.Weather.Should().BeNull();
        sheet.Invoices.Should().BeEmpty();
        sheet.Points.Should().BeEmpty();
        CallSheets.Build(Customer, unknown, Today).Should().BeNull();
    }
}
