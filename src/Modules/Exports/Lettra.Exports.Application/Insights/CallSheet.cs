using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Application.Insights;

/// <summary>La fiche à lire avant d'appeler un client en retard.</summary>
/// <param name="Dossier">Le dossier courant.</param>
/// <param name="Customer">Le client à appeler.</param>
public sealed record GetCallSheetQuery(DossierId Dossier, Guid Customer) : IQuery<Result<CallSheetResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Ce que la fiche sait du client, lu par le cas d'usage puis remis au calcul.</summary>
/// <param name="Customer">Le client (nom, adresse, professionnel ou non), s'il existe.</param>
/// <param name="History">Ses factures et acomptes validés et leurs encaissements.</param>
/// <param name="Platform">Le dernier statut à la plateforme de chaque pièce transmise.</param>
/// <param name="Reminders">Ses relances faites, les plus récentes d'abord.</param>
public sealed record CallSheetInput(
    ReminderCustomer? Customer,
    IReadOnlyList<PaymentHistoryInvoice> History,
    IReadOnlyDictionary<Guid, PlatformStatusDto> Platform,
    IReadOnlyList<PaymentReminder> Reminders);

/// <summary>
/// Le calcul de la fiche d'appel, pur et déterministe. Il reprend la météo des paiements pour les habitudes du client et
/// l'historique des relances, puis ordonne les points à aborder : litiges d'abord (on ne réclame pas une facture
/// contestée), factures douteuses, dernière relance déjà partie, aucune relance encore, dernière relance, indemnité de
/// 40 € d'un client professionnel, habitude, dernier encaissement.
/// </summary>
public static class CallSheets
{
    private static readonly PlatformStatusDto[] Contested = [PlatformStatusDto.Disputed, PlatformStatusDto.Refused];

    public static CallSheetResponse? Build(Guid customerId, CallSheetInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        PaymentHistoryInvoice[] history = [.. input.History.Where(i => i.CustomerId == customerId)];
        string? name = input.Customer?.Name ?? history.FirstOrDefault()?.CustomerName;
        if (name is null)
        {
            return null;
        }

        CustomerPaymentWeatherResponse? weather = history.Length == 0
            ? null
            : PaymentWeatherForecast.Compute(history, today).Customers.FirstOrDefault(c => c.CustomerId == customerId);
        Dictionary<Guid, PaymentReminder> lastByInvoice = input.Reminders
            .GroupBy(r => r.InvoiceId.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.OccurredAt).First());

        CallSheetInvoiceResponse[] invoices =
        [
            .. history
                .Where(i => i.Outstanding > 0m)
                .Select(i => new CallSheetInvoiceResponse(
                    i.InvoiceId,
                    i.Number,
                    i.IssueDate,
                    i.DueDate,
                    i.Outstanding,
                    i.DueDate is { } due && due < today ? today.DayNumber - due.DayNumber : 0,
                    input.Platform.TryGetValue(i.InvoiceId, out PlatformStatusDto status) ? status : null,
                    lastByInvoice.TryGetValue(i.InvoiceId, out PaymentReminder? last) ? (ReminderLevelDto)(int)last.Level : null,
                    last?.OccurredOn))
                .OrderByDescending(i => i.DaysLate)
                .ThenBy(i => i.Number, StringComparer.Ordinal),
        ];
        CallSheetReminderResponse[] reminders =
        [
            .. input.Reminders
                .OrderByDescending(r => r.OccurredAt)
                .Take(CallSheetResponse.MaxReminders)
                .Select(r => new CallSheetReminderResponse(r.OccurredOn, r.InvoiceNumber, (ReminderLevelDto)(int)r.Level, (ReminderChannelDto)(int)r.Channel)),
        ];

        return new CallSheetResponse(
            today,
            customerId,
            name,
            input.Customer?.Email,
            weather,
            invoices.Sum(i => i.OutstandingInclTax),
            invoices.Where(i => i.DaysLate > 0).Sum(i => i.OutstandingInclTax),
            invoices,
            reminders,
            [.. Points(input, history, invoices, weather)]);
    }

    private static IEnumerable<CallPointResponse> Points(
        CallSheetInput input,
        PaymentHistoryInvoice[] history,
        CallSheetInvoiceResponse[] invoices,
        CustomerPaymentWeatherResponse? weather)
    {
        foreach (CallSheetInvoiceResponse contested in invoices.Where(i => i.PlatformStatus is { } s && Contested.Contains(s)))
        {
            yield return new CallPointResponse(CallPointKindDto.Disputed, Number: contested.Number, Amount: contested.OutstandingInclTax);
        }

        CallSheetInvoiceResponse[] late = [.. invoices.Where(i => i.DaysLate > 0)];
        CallSheetInvoiceResponse[] doubtful = [.. late.Where(i => i.DaysLate > PaymentWeatherForecast.StormOverdueDays)];
        if (doubtful.Length > 0)
        {
            yield return new CallPointResponse(
                CallPointKindDto.Doubtful, Count: doubtful.Length, Days: doubtful.Max(i => i.DaysLate), Amount: doubtful.Sum(i => i.OutstandingInclTax));
        }

        PaymentReminder? lastReminder = input.Reminders.OrderByDescending(r => r.OccurredAt).FirstOrDefault();
        CallSheetInvoiceResponse[] finalSent = [.. late.Where(i => i.LastReminderLevel is ReminderLevelDto.Final or ReminderLevelDto.FormalNotice)];
        if (finalSent.Length > 0)
        {
            yield return new CallPointResponse(CallPointKindDto.FinalSent, Number: finalSent[0].Number, Count: finalSent.Length);
        }

        if (late.Length > 0 && late.All(i => i.LastReminderLevel is null))
        {
            yield return new CallPointResponse(CallPointKindDto.NeverReminded, Count: late.Length, Amount: late.Sum(i => i.OutstandingInclTax));
        }
        else if (lastReminder is not null)
        {
            yield return new CallPointResponse(
                CallPointKindDto.LastReminder, Number: lastReminder.InvoiceNumber, Date: lastReminder.OccurredOn, Level: (ReminderLevelDto)(int)lastReminder.Level);
        }

        if (late.Length > 0 && input.Customer?.IsProfessional == true)
        {
            yield return new CallPointResponse(
                CallPointKindDto.Indemnity, Count: late.Length, Amount: late.Length * MandatoryMentions.RecoveryIndemnityAmount);
        }

        if (weather is { Weather: not PaymentWeatherDto.Unknown })
        {
            yield return new CallPointResponse(CallPointKindDto.Habit, Days: weather.TypicalDaysLate, Percent: weather.OnTimePercent);
        }

        PaymentHistoryEntry? lastPayment = history.SelectMany(i => i.Payments).OrderByDescending(p => p.Date).FirstOrDefault();
        if (lastPayment is not null)
        {
            yield return new CallPointResponse(CallPointKindDto.LastPayment, Amount: lastPayment.Amount, Date: lastPayment.Date);
        }
    }
}

internal sealed class GetCallSheetHandler(
    IPaymentHistoryReader history,
    IReceivablesReader receivables,
    IReminderContextReader customers,
    IPaymentReminderRepository reminders,
    TimeProvider clock) : IQueryHandler<GetCallSheetQuery, Result<CallSheetResponse>>
{
    private const int ReminderPageSize = 1_000;

    public async Task<Result<CallSheetResponse>> HandleAsync(GetCallSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CustomerId customer = new(query.Customer);
        IReadOnlyDictionary<CustomerId, ReminderCustomer> known = await customers.CustomersAsync([customer], cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PaymentHistoryInvoice> invoices = await history.ReadAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, PlatformStatusDto> platform = (await receivables.ReadAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.Transmission is not null)
            .ToDictionary(r => r.InvoiceId, r => r.Transmission!.Status.ToDto());
        (IReadOnlyList<ReminderHistoryItem> items, _) = await reminders.ListAsync(
            new ReminderHistoryFilter(null, customer, null, ReminderOutcome.Sent, null, null, null, null, ReminderSortDto.Date, Descending: true, 1, ReminderPageSize),
            cancellationToken).ConfigureAwait(false);

        CallSheetInput input = new(known.GetValueOrDefault(customer), invoices, platform, [.. items.Select(i => i.Reminder)]);
        return CallSheets.Build(query.Customer, input, LegalCalendar.Today(clock)) is { } sheet
            ? sheet
            : Error.NotFound(ErrorCodes.Customer.NotFound);
    }
}
