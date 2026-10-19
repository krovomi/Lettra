using System.Diagnostics;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Pricing;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.DemoData;

/// <summary>Ce qu'un dossier a reçu.</summary>
public sealed record DemoDossierReport(
    Guid DossierId,
    DemoBusiness Business,
    int Customers,
    int Suppliers,
    int CatalogItems,
    int PriceLists,
    int Quotes,
    int Invoices,
    int CreditNotes,
    int Transmissions,
    int Payments,
    TimeSpan Duration);

/// <summary>Une étape du chargement a été refusée par l'application : le jeu ne contourne jamais un contrôle.</summary>
public sealed class DemoDataException(string message) : Exception(message);

/// <summary>
/// Rejoue un <see cref="DemoScenario"/> dans un dossier, étape par étape et dans l'ordre chronologique, par les
/// gateways des écrans (donc par les cas d'usage, leur pipeline de droits, de validation et de conformité). Numéros,
/// chaîne d'intégrité, écritures et journal des transmissions sont ceux qu'aurait produits un utilisateur.
/// </summary>
internal sealed class DemoDossierLoader(IServiceProvider services, DemoClock clock)
{
    private readonly ICustomersGateway _customers = services.GetRequiredService<ICustomersGateway>();
    private readonly ISuppliersGateway _suppliers = services.GetRequiredService<ISuppliersGateway>();
    private readonly ICatalogGateway _catalog = services.GetRequiredService<ICatalogGateway>();
    private readonly IPriceListsGateway _priceLists = services.GetRequiredService<IPriceListsGateway>();
    private readonly IQuotesGateway _quotes = services.GetRequiredService<IQuotesGateway>();
    private readonly IInvoicesGateway _invoices = services.GetRequiredService<IInvoicesGateway>();
    private readonly IPlatformTransmissionGateway _platform = services.GetRequiredService<IPlatformTransmissionGateway>();
    private readonly IBillingSettingsGateway _settings = services.GetRequiredService<IBillingSettingsGateway>();

    private readonly List<Step> _steps = [];
    private Guid[] _items = [];
    private Guid[] _customerIds = [];
    private int _quoteCount;
    private int _invoiceCount;
    private int _creditCount;
    private int _transmissionCount;
    private int _paymentCount;

    /// <summary>Vrai si le dossier ouvert a déjà des devis ou des factures : le jeu ne se mélange pas à de vraies saisies.</summary>
    public async Task<bool> HasDocumentsAsync(CancellationToken cancellationToken)
    {
        PagedResponse<QuoteSummary> quotes = Ok(await _quotes.ListAsync(new BillingListQuery(PageSize: 1), null, cancellationToken).ConfigureAwait(false), "lecture des devis");
        PagedResponse<InvoiceSummary> invoices = Ok(await _invoices.ListAsync(new BillingListQuery(PageSize: 1), null, null, cancellationToken).ConfigureAwait(false), "lecture des factures");
        return quotes.TotalCount > 0 || invoices.TotalCount > 0;
    }

    public async Task<DemoDossierReport> LoadAsync(Guid dossierId, DemoScenario scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        Stopwatch watch = Stopwatch.StartNew();
        _items = new Guid[scenario.Items.Count];
        _customerIds = new Guid[scenario.Customers.Count];

        Plan(scenario.StartsAt, ct => SaveProfileAsync(scenario.Profile, ct));
        for (int i = 0; i < scenario.Items.Count; i++)
        {
            int index = i;
            Plan(scenario.Items[i].CreatedAt, async ct =>
                _items[index] = Ok(await _catalog.CreateAsync(scenario.Items[index].Request, ct).ConfigureAwait(false), "article").Id);
        }

        for (int i = 0; i < scenario.Customers.Count; i++)
        {
            int index = i;
            Plan(scenario.Customers[i].CreatedAt, async ct =>
                _customerIds[index] = Ok(await _customers.CreateAsync(scenario.Customers[index].Request, ct).ConfigureAwait(false), "client").Id);
        }

        foreach (DemoSupplier supplier in scenario.Suppliers)
        {
            Plan(supplier.CreatedAt, async ct => Ok(await _suppliers.CreateAsync(supplier.Request, ct).ConfigureAwait(false), "fournisseur"));
        }

        foreach (DemoPriceList list in scenario.PriceLists)
        {
            Plan(list.CreatedAt, async ct => Ok(await _priceLists.CreateAsync(
                new SavePriceListRequest
                {
                    Name = list.Name,
                    CustomerIds = [.. list.Customers.Select(c => _customerIds[c])],
                    Entries = [.. list.Items.Select(i => new PriceListEntryRequest { CatalogItemId = _items[i], DiscountPercent = list.DiscountPercent })],
                },
                ct).ConfigureAwait(false), "grille tarifaire"));
        }

        foreach (DemoQuoteStory story in scenario.Quotes)
        {
            PlanQuote(story, scenario);
        }

        foreach (DemoInvoiceStory story in scenario.Invoices)
        {
            InvoiceState state = new();
            Plan(story.Invoice.CreatedAt, async ct =>
            {
                state.Current = Ok(await _invoices.CreateAsync(
                    new SaveInvoiceRequest
                    {
                        CustomerId = _customerIds[story.Customer],
                        Subject = story.Subject,
                        Lines = [.. story.Lines.Select(l => l.ToRequest(scenario.Items, _items[l.Item]))],
                    },
                    ct).ConfigureAwait(false), "facture");
                _invoiceCount++;
            });
            PlanInvoice(story.Invoice, state);
        }

        foreach (int archived in scenario.ArchivedItems)
        {
            Plan(scenario.ArchivedAt, async ct =>
            {
                Guid id = _items[archived];
                long version = Ok(await _catalog.ListAsync(new CatalogListQuery(scenario.Items[archived].Request.Reference, PageSize: 5), ct).ConfigureAwait(false), "catalogue")
                    .Items.Single(i => i.Id == id).Version;
                Ok(await _catalog.SetArchivedAsync(id, version, isArchived: true, ct).ConfigureAwait(false), "archivage d'un article");
            });
        }

        foreach (Step step in _steps.OrderBy(s => s.At).ThenBy(s => s.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Set(step.At);
            await step.Run(cancellationToken).ConfigureAwait(false);
        }

        return new DemoDossierReport(
            dossierId,
            scenario.Business,
            scenario.Customers.Count,
            scenario.Suppliers.Count,
            scenario.Items.Count,
            scenario.PriceLists.Count,
            _quoteCount,
            _invoiceCount,
            _creditCount,
            _transmissionCount,
            _paymentCount,
            watch.Elapsed);
    }

    private async Task SaveProfileAsync(SaveCompanyProfileRequest profile, CancellationToken cancellationToken)
    {
        // Une fiche déjà remplie à la main reste celle de l'utilisateur.
        OperationResult<CompanyProfileResponse> current = await _settings.GetCompanyProfileAsync(cancellationToken).ConfigureAwait(false);
        if (current is { IsSuccess: true, Value: not null })
        {
            return;
        }

        Ok(await _settings.SaveCompanyProfileAsync(profile, null, cancellationToken).ConfigureAwait(false), "fiche entreprise");
    }

    private void PlanQuote(DemoQuoteStory story, DemoScenario scenario)
    {
        QuoteResponse? quote = null;
        Plan(story.CreatedAt, async ct =>
        {
            quote = Ok(await _quotes.CreateAsync(
                new SaveQuoteRequest
                {
                    CustomerId = _customerIds[story.Customer],
                    Subject = story.Subject,
                    Lines = [.. story.Lines.Select(l => l.ToRequest(scenario.Items, _items[l.Item]))],
                },
                ct).ConfigureAwait(false), "devis");
            _quoteCount++;
        });

        if (story.ValidatedAt is { } validated)
        {
            Plan(validated, async ct => quote = Ok(await _quotes.ValidateAsync(quote!.Id, quote.Version, ct).ConfigureAwait(false), "validation d'un devis"));
        }

        if (story.Outcome is { } outcome)
        {
            Plan(outcome.At, async ct => quote = Ok(await _quotes.SetOutcomeAsync(quote!.Id, outcome.Accepted, quote.Version, ct).ConfigureAwait(false), "réponse du client"));
        }

        if (story.DownPayment is { } downPayment)
        {
            InvoiceState state = new();
            Plan(downPayment.Invoice.CreatedAt, async ct =>
            {
                state.Current = Ok(await _quotes.CreateDownPaymentAsync(quote!.Id, new CreateDownPaymentRequest { Percent = downPayment.Percent }, ct).ConfigureAwait(false), "facture d'acompte");
                _invoiceCount++;
            });
            PlanInvoice(downPayment.Invoice, state);
        }

        if (story.FinalInvoice is { } final)
        {
            InvoiceState state = new();
            Plan(final.CreatedAt, async ct =>
            {
                state.Current = Ok(await _quotes.CreateInvoiceAsync(quote!.Id, ct).ConfigureAwait(false), "facture du devis");
                _invoiceCount++;
            });
            PlanInvoice(final, state);
        }
    }

    /// <summary>Validation, transmission à la plateforme, avoir et encaissements d'une facture créée par une étape précédente.</summary>
    private void PlanInvoice(DemoSettledInvoice invoice, InvoiceState state)
    {
        if (invoice.ValidatedAt is not { } validated)
        {
            return;
        }

        Plan(validated, async ct => state.Current = Ok(await _invoices.ValidateAsync(state.Current!.Id, state.Current.Version, ct).ConfigureAwait(false), "validation d'une facture"));
        if (invoice.TransmittedAt is not { } transmitted)
        {
            return;
        }

        Plan(transmitted, ct => TransmitAsync(state.Current!.Id, ct));
        if (invoice.Credit is { } credit)
        {
            Plan(credit.At, async ct =>
            {
                InvoiceResponse original = Ok(await _invoices.GetAsync(state.Current!.Id, ct).ConfigureAwait(false), "facture à créditer");
                CreateCreditNoteRequest request = new() { Reason = credit.Reason };
                if (credit.Line is int position && original.Lines.FirstOrDefault(l => l.Position == position && l.Quantity > 0) is { } line)
                {
                    decimal quantity = line.Quantity >= 2 ? Math.Floor(line.Quantity / 2) : line.Quantity;
                    request.Lines = [new CreditNoteLineRequest(position, quantity)];
                }

                InvoiceResponse draft = Ok(await _invoices.CreateCreditNoteAsync(original.Id, request, ct).ConfigureAwait(false), "avoir");
                InvoiceResponse validatedCredit = Ok(await _invoices.ValidateAsync(draft.Id, draft.Version, ct).ConfigureAwait(false), "validation d'un avoir");
                state.Credited += validatedCredit.Totals.TotalInclTax;
                _creditCount++;
                await TransmitAsync(validatedCredit.Id, ct).ConfigureAwait(false);
            });
        }

        foreach (DemoPayment payment in invoice.Payments)
        {
            Plan(payment.At, async ct =>
            {
                PlatformTransmissionResponse transmission = Ok(await _platform.GetAsync(state.Current!.Id, ct).ConfigureAwait(false), "transmission");
                decimal remaining = state.Current.Totals.TotalInclTax - state.Credited - transmission.CollectedAmount;
                decimal amount = payment.ShareOfRemaining >= 1m ? remaining : decimal.Round(remaining * payment.ShareOfRemaining, 2);
                if (amount <= 0m || transmission.IsFinal)
                {
                    return;
                }

                DateOnly date = LegalCalendar.DateOf(payment.At);
                Ok(await _platform.ReportPaymentAsync(state.Current.Id, new ReportPaymentRequest { Date = date, Amount = amount }, ct).ConfigureAwait(false), "encaissement");
                _paymentCount++;
            });
        }
    }

    /// <summary>Dépôt sur la plateforme (la facture passe émise), puis lecture des statuts : reçue, mise à disposition.</summary>
    private async Task TransmitAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        Ok(await _platform.TransmitAsync(invoiceId, new TransmitInvoiceRequest(), cancellationToken).ConfigureAwait(false), "transmission à la plateforme");
        Ok(await _platform.RefreshAsync(invoiceId, cancellationToken).ConfigureAwait(false), "statuts de la plateforme");
        _transmissionCount++;
    }

    private void Plan(DateTimeOffset at, Func<CancellationToken, Task> run) => _steps.Add(new Step(at, _steps.Count, run));

    private static T Ok<T>(OperationResult<T> result, string what)
    {
        if (result is { IsSuccess: true, Value: { } value })
        {
            return value;
        }

        throw new DemoDataException($"Jeu de démonstration : {what} refusé ({string.Join(", ", result.Errors.Select(e => e.Code))}).");
    }

    private sealed record Step(DateTimeOffset At, int Order, Func<CancellationToken, Task> Run);

    /// <summary>La facture d'une histoire, telle que la dernière étape l'a laissée, et ce qu'en ont retiré ses avoirs.</summary>
    private sealed class InvoiceState
    {
        public InvoiceResponse? Current { get; set; }

        public decimal Credited { get; set; }
    }
}
