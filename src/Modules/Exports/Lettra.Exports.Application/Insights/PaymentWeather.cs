using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Insights;

/// <summary>Un encaissement imputé sur une facture : sa date et sa part.</summary>
public sealed record PaymentHistoryEntry(DateOnly Date, decimal Amount);

/// <summary>
/// Une facture ou un acompte validé vu par la météo des paiements : ce qui était dû (TTC moins les avoirs), ce qui
/// reste dû, et les encaissements connus, datés.
/// </summary>
public sealed record PaymentHistoryInvoice(
    Guid InvoiceId,
    Guid CustomerId,
    string CustomerName,
    string Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal AmountDue,
    decimal Outstanding,
    IReadOnlyList<PaymentHistoryEntry> Payments);

/// <summary>
/// Lecture de l'historique de paiement du dossier (lecture seule) : factures et acomptes validés, avec leurs
/// encaissements enregistrés dans Lettra, ou à défaut ceux déclarés à la plateforme agréée.
/// </summary>
public interface IPaymentHistoryReader
{
    Task<IReadOnlyList<PaymentHistoryInvoice>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>La météo des paiements du dossier : un bulletin par client et la prévision des encaissements.</summary>
public sealed record GetPaymentWeatherQuery(DossierId Dossier) : IQuery<Result<PaymentWeatherResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Le calcul de la météo, en statistiques simples et déterministes (aucune IA) :
/// <list type="bullet">
/// <item>chaque facture réglée dans les douze derniers mois compte pour son retard : jours entre l'échéance et le dernier
/// encaissement ; chaque facture échue encore due compte pour son retard à ce jour ;</item>
/// <item>le retard moyen est pondéré par les montants dus ; le retard habituel est la médiane ;</item>
/// <item>soleil jusqu'à <see cref="SunnyMaxDays"/> jours de retard moyen (le temps d'un virement), nuages jusqu'à
/// <see cref="CloudyMaxDays"/>, pluie jusqu'à <see cref="RainyMaxDays"/>, orage au-delà, ou dès qu'une facture a plus de
/// <see cref="StormOverdueDays"/> jours de retard ; moins de <see cref="MinimumSamples"/> factures : pas de météo ;</item>
/// <item>une facture due est attendue à son échéance décalée du retard habituel, jamais avant demain ; une facture de
/// plus de <see cref="StormOverdueDays"/> jours de retard est douteuse : elle sort de la prévision et se compte à part ;</item>
/// <item>le meilleur jour d'envoi est le jour d'émission après lequel le client a payé le plus vite, s'il a au moins
/// <see cref="MinimumSamples"/> factures ce jour-là et un autre jour pour comparer, et s'il fait gagner au moins
/// <see cref="BestDayMinimumGain"/> jours.</item>
/// </list>
/// </summary>
public static class PaymentWeatherForecast
{
    public const int MinimumSamples = 2;
    public const int SunnyMaxDays = 2;
    public const int CloudyMaxDays = 10;
    public const int RainyMaxDays = 30;
    public const int StormOverdueDays = PaymentWeatherResponse.StormOverdueDays;
    public const int HistoryDays = 365;
    public const int BestDayMinimumGain = 2;

    public static PaymentWeatherResponse Compute(IEnumerable<PaymentHistoryInvoice> invoices, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        CustomerPaymentWeatherResponse[] customers =
        [
            .. invoices
                .Where(i => i.AmountDue > 0m && (i.DueDate ?? i.IssueDate) is not null)
                .GroupBy(i => i.CustomerId)
                .Select(g => ForCustomer([.. g], today))
                .Where(c => c.Samples > 0 || c.Expected.Count > 0)
                .OrderByDescending(c => Severity(c.Weather))
                .ThenByDescending(c => c.OutstandingInclTax)
                .ThenBy(c => c.CustomerName, StringComparer.CurrentCulture),
        ];

        // Rien n'est attendu avant demain : la prévision part du lundi de la semaine de demain.
        DateOnly tomorrow = today.AddDays(1);
        DateOnly firstWeek = tomorrow.AddDays(-(((int)tomorrow.DayOfWeek + 6) % 7));
        ExpectedCollectionResponse[] all = [.. customers.SelectMany(c => c.Expected)];
        ExpectedCollectionResponse[] expected = [.. all.Where(e => !e.Doubtful)];
        CollectionForecastWeekResponse[] forecast =
        [
            .. Enumerable.Range(0, PaymentWeatherResponse.ForecastWeeks).Select(week =>
            {
                DateOnly start = firstWeek.AddDays(7 * week);
                ExpectedCollectionResponse[] inWeek = [.. expected.Where(e => e.ExpectedDate >= start && e.ExpectedDate < start.AddDays(7))];
                return new CollectionForecastWeekResponse(start, inWeek.Sum(e => e.OutstandingInclTax), inWeek.Length);
            }),
        ];
        DateOnly horizon = firstWeek.AddDays(7 * PaymentWeatherResponse.ForecastWeeks);
        return new PaymentWeatherResponse(
            today,
            customers,
            forecast,
            expected.Where(e => e.ExpectedDate >= horizon).Sum(e => e.OutstandingInclTax),
            all.Where(e => e.Doubtful).Sum(e => e.OutstandingInclTax));
    }

    /// <summary>Rang d'une météo, de la plus inquiétante à l'inconnue, pour trier les clients.</summary>
    public static int Severity(PaymentWeatherDto weather) => weather switch
    {
        PaymentWeatherDto.Stormy => 4,
        PaymentWeatherDto.Rainy => 3,
        PaymentWeatherDto.Cloudy => 2,
        PaymentWeatherDto.Sunny => 1,
        _ => 0,
    };

    private static CustomerPaymentWeatherResponse ForCustomer(IReadOnlyList<PaymentHistoryInvoice> invoices, DateOnly today)
    {
        List<Sample> samples = [];
        foreach (PaymentHistoryInvoice invoice in invoices)
        {
            DateOnly due = (invoice.DueDate ?? invoice.IssueDate)!.Value;
            if (invoice.Outstanding <= 0m && invoice.Payments.Count > 0)
            {
                DateOnly settled = invoice.Payments.Max(p => p.Date);
                if (settled.DayNumber >= today.DayNumber - HistoryDays)
                {
                    samples.Add(new Sample(invoice, settled.DayNumber - due.DayNumber, settled, Open: false));
                }
            }
            else if (invoice.Outstanding > 0m && due < today)
            {
                samples.Add(new Sample(invoice, today.DayNumber - due.DayNumber, null, Open: true));
            }
        }

        int maxOverdue = samples.Where(s => s.Open).Select(s => s.DaysLate).DefaultIfEmpty(0).Max();
        int average = samples.Count == 0 ? 0 : (int)Math.Round(samples.Sum(s => s.Invoice.AmountDue * s.DaysLate) / samples.Sum(s => s.Invoice.AmountDue), MidpointRounding.AwayFromZero);
        int[] sorted = [.. samples.Select(s => s.DaysLate).Order()];
        int typical = sorted.Length == 0 ? 0 : sorted[(sorted.Length - 1) / 2];
        int onTime = samples.Count == 0 ? 0 : (int)Math.Round(100m * samples.Count(s => !s.Open && s.DaysLate <= 0) / samples.Count, MidpointRounding.AwayFromZero);
        PaymentWeatherDto weather = Weather(samples.Count, average, maxOverdue);
        (DayOfWeek? bestDay, int gain) = BestSendDay(samples);

        int shift = weather == PaymentWeatherDto.Unknown ? 0 : Math.Max(0, typical);
        ExpectedCollectionResponse[] expected =
        [
            .. invoices
                .Where(i => i.Outstanding > 0m)
                .Select(i =>
                {
                    DateOnly due = (i.DueDate ?? i.IssueDate)!.Value;
                    DateOnly expectedDate = due.AddDays(shift);
                    bool doubtful = today.DayNumber - due.DayNumber > StormOverdueDays;
                    return new ExpectedCollectionResponse(
                        i.InvoiceId, i.Number, i.DueDate, i.Outstanding, expectedDate > today ? expectedDate : today.AddDays(1), doubtful);
                })
                .OrderBy(e => e.ExpectedDate)
                .ThenBy(e => e.Number, StringComparer.Ordinal),
        ];

        PaymentHistoryInvoice first = invoices[0];
        return new CustomerPaymentWeatherResponse(
            first.CustomerId,
            first.CustomerName,
            weather,
            samples.Count,
            average,
            typical,
            onTime,
            maxOverdue,
            bestDay,
            gain,
            expected.Sum(e => e.OutstandingInclTax),
            expected);
    }

    private static PaymentWeatherDto Weather(int samples, int averageDaysLate, int maxDaysOverdue)
    {
        if (maxDaysOverdue > StormOverdueDays)
        {
            return PaymentWeatherDto.Stormy;
        }

        if (samples < MinimumSamples)
        {
            return PaymentWeatherDto.Unknown;
        }

        return averageDaysLate switch
        {
            <= SunnyMaxDays => PaymentWeatherDto.Sunny,
            <= CloudyMaxDays => PaymentWeatherDto.Cloudy,
            <= RainyMaxDays => PaymentWeatherDto.Rainy,
            _ => PaymentWeatherDto.Stormy,
        };
    }

    /// <summary>Le jour d'émission après lequel le client a payé le plus vite, comparé à ses autres factures réglées.</summary>
    private static (DayOfWeek? Day, int Gain) BestSendDay(IReadOnlyList<Sample> samples)
    {
        var paid = samples
            .Where(s => s is { Open: false, Invoice.IssueDate: not null })
            .Select(s => (Day: s.Invoice.IssueDate!.Value.DayOfWeek, Delay: s.Settled!.Value.DayNumber - s.Invoice.IssueDate!.Value.DayNumber))
            .ToArray();
        var days = paid
            .GroupBy(p => p.Day)
            .Where(g => g.Count() >= MinimumSamples)
            .Select(g => (Day: g.Key, Average: g.Average(p => p.Delay)))
            .OrderBy(d => d.Average)
            .ThenBy(d => ((int)d.Day + 6) % 7)
            .ToArray();
        if (days.Length == 0 || paid.All(p => p.Day == days[0].Day))
        {
            return (null, 0);
        }

        double others = paid.Where(p => p.Day != days[0].Day).Average(p => p.Delay);
        int gain = (int)Math.Round(others - days[0].Average, MidpointRounding.AwayFromZero);
        return gain >= BestDayMinimumGain ? (days[0].Day, gain) : (null, 0);
    }

    private sealed record Sample(PaymentHistoryInvoice Invoice, int DaysLate, DateOnly? Settled, bool Open);
}

internal sealed class GetPaymentWeatherHandler(IPaymentHistoryReader reader, TimeProvider clock)
    : IQueryHandler<GetPaymentWeatherQuery, Result<PaymentWeatherResponse>>
{
    public async Task<Result<PaymentWeatherResponse>> HandleAsync(GetPaymentWeatherQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<PaymentHistoryInvoice> invoices = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return PaymentWeatherForecast.Compute(invoices, LegalCalendar.Today(clock));
    }
}
