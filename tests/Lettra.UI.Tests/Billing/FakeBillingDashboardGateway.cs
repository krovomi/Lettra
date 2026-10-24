using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Billing;

/// <summary>
/// L'accueil calculé comme au serveur (<see cref="BillingDashboardCalculator"/>) à partir des pièces des passerelles
/// factices : un test qui ajoute une facture la voit dans les chiffres, sans les recopier.
/// </summary>
internal sealed class FakeBillingDashboardGateway(FakeInvoicesGateway invoices, FakeQuotesGateway quotes, TimeProvider clock)
    : IBillingDashboardGateway
{
    public bool Offline { get; set; }

    public Task<OperationResult<BillingDashboardResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Task.FromResult(OperationResult<BillingDashboardResponse>.Failure(ErrorCodes.Api.Unreachable));
        }

        BillingKpiFacts facts = new(
            [
                .. invoices.Invoices.Values
                    .Where(i => i is { Status: not InvoiceStatusDto.Draft, IssueDate: not null })
                    .Select(i => new InvoiceKpiFact((InvoiceKind)i.Kind, i.IssueDate!.Value, i.DueDate, i.CustomerId,
                        i.Totals.TotalExclTax, i.Totals.TotalInclTax, 0m, 0m)),
            ],
            [
                .. quotes.Quotes.Values
                    .Where(q => q is { Status: not QuoteStatusDto.Draft, IssueDate: not null })
                    .Select(q => new QuoteKpiFact((QuoteStatus)q.Status, q.IssueDate!.Value, q.ValidUntil, q.Totals.TotalExclTax)),
            ]);
        return Task.FromResult(OperationResult<BillingDashboardResponse>.Success(
            BillingDashboardCalculator.Compute(facts, LegalCalendar.DateOf(clock.GetUtcNow()))));
    }
}

internal static class FakeBillingDashboardRegistration
{
    /// <summary>L'accueil tiré des passerelles factices déjà enregistrées (ou de nouvelles, quand le test n'en a pas).</summary>
    public static IServiceCollection AddFakeBillingDashboard(this IServiceCollection services) =>
        services.AddSingleton<IBillingDashboardGateway>(sp => new FakeBillingDashboardGateway(
            sp.GetService<IInvoicesGateway>() as FakeInvoicesGateway ?? new FakeInvoicesGateway(),
            sp.GetService<IQuotesGateway>() as FakeQuotesGateway ?? new FakeQuotesGateway(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
}
