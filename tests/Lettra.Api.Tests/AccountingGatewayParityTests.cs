using Lettra.Contracts.Accounting;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Accounting;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Révision avant clôture par la même interface, en HTTP et en direct.</summary>
public sealed class AccountingGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_revision_lit_les_ecritures_les_creances_la_banque_et_les_brouillons_du_dossier(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IAccountingGateway accounting = services.GetRequiredService<IAccountingGateway>();
        await GatewayHarness.ValidatedInvoiceAsync(services);
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        await services.GetRequiredService<IInvoicesGateway>().CreateAsync(
            new SaveInvoiceRequest { CustomerId = customer, Lines = [GatewayHarness.Conseil()] }, default);

        OperationResult<ClosingReviewResponse> today = await accounting.GetClosingReviewAsync(Today, default);
        OperationResult<ClosingReviewResponse> lastClosing = await accounting.GetClosingReviewAsync(null, default);

        today.IsSuccess.Should().BeTrue(GatewayHarness.Codes(today));
        today.Value!.ClosingDate.Should().Be(Today);
        today.Value.Checks.Should().HaveCount(7);
        ReviewCheckResponse vat = today.Value.Checks.Single(c => c.Code == ClosingReview.VatRate);
        vat.Severity.Should().Be(ReviewSeverityDto.Ok);
        vat.Measure.Should().Be(20m);
        today.Value.Checks.Single(c => c.Code == ClosingReview.OldDrafts).Items.Should().Contain(i => i.Label == "Boulangerie Martin");
        lastClosing.Value!.ClosingDate.Should().BeBefore(Today);
        lastClosing.Value.Checks.Single(c => c.Code == ClosingReview.OldDrafts).Severity.Should().Be(ReviewSeverityDto.Ok);
    }

    public void Dispose() => _harness.Dispose();
}
