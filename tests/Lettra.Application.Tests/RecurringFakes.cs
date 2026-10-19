using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Modèles de factures récurrentes en mémoire, pour que le conteneur des tests se valide.</summary>
internal sealed class InMemoryRecurringInvoices : IRecurringInvoiceRepository, IRecurringInvoiceReadStore
{
    private readonly ConcurrentDictionary<RecurringInvoiceId, RecurringInvoice> _templates = new();

    public Task AddAsync(RecurringInvoice template, CancellationToken cancellationToken)
    {
        _templates[template.Id] = template;
        return Task.CompletedTask;
    }

    public Task<RecurringInvoice?> FindAsync(RecurringInvoiceId id, CancellationToken cancellationToken) =>
        Task.FromResult(_templates.GetValueOrDefault(id));

    public Task<IReadOnlyList<RecurringInvoiceId>> ListDueAsync(DateOnly today, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecurringInvoiceId>>([.. _templates.Values.Where(t => t.DuePeriod(today) is not null).Select(t => t.Id)]);

    public Task RemoveAsync(RecurringInvoice template, CancellationToken cancellationToken)
    {
        _templates.TryRemove(template.Id, out _);
        return Task.CompletedTask;
    }

    public Task<PagedResponse<RecurringInvoiceSummary>> ListAsync(RecurringInvoiceListFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResponse<RecurringInvoiceSummary>([], filter.Page, filter.PageSize, 0));

    public Task<IReadOnlyDictionary<InvoiceId, RecurringInvoiceState>> InvoicesAsync(IReadOnlyCollection<InvoiceId> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<InvoiceId, RecurringInvoiceState>>(new Dictionary<InvoiceId, RecurringInvoiceState>());
}

internal static class RecurringFakesRegistration
{
    public static IServiceCollection AddRecurringFakes(this IServiceCollection services)
    {
        InMemoryRecurringInvoices store = new();
        services.AddSingleton<IRecurringInvoiceRepository>(store);
        services.AddSingleton<IRecurringInvoiceReadStore>(store);
        return services;
    }
}
