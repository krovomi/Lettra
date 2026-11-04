using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Devis à relancer et carnet de commandes : mêmes réponses et mêmes refus par HTTP et en direct.</summary>
public sealed partial class GatewayParityTests
{
    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Devis_a_relancer_memes_reponses_et_memes_refus(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IQuoteFollowUpsGateway followUps = services.GetRequiredService<IQuoteFollowUpsGateway>();
        Guid unknown = Guid.CreateVersion7();

        (await followUps.GetOverviewAsync(default)).Value!.Settings.EmailConfigured.Should().BeFalse();
        (await followUps.GetPreviewAsync(unknown, default)).Errors.Single().Code.Should().Be(ErrorCodes.Quote.NotFound);
        (await followUps.SendAsync(unknown, new SendQuoteFollowUpRequest { Channel = QuoteFollowUpChannelDto.Manual }, default))
            .Errors.Single().Code.Should().Be(ErrorCodes.Quote.NotFound);

        OperationResult<PagedResponse<QuoteFollowUpResponse>> history = await followUps.ListHistoryAsync(new QuoteFollowUpHistoryQuery(
            unknown, QuoteFollowUpChannelDto.Email, QuoteFollowUpOutcomeDto.Failed, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            "Dupont & fils", QuoteFollowUpSortDto.Amount, false, 1, 10), default);
        history.IsSuccess.Should().BeTrue();
        history.Value!.TotalCount.Should().Be(0);

        (await followUps.UpdateSettingsAsync(new UpdateQuoteFollowUpSettingsRequest { WindowDays = 0 }, default))
            .Errors.Single().Code.Should().Be(ErrorCodes.QuoteFollowUps.WindowInvalid);
        (await followUps.UpdateSettingsAsync(new UpdateQuoteFollowUpSettingsRequest { WindowDays = 6 }, default)).Value!.WindowDays.Should().Be(6);

        OperationResult<OrderBookResponse> book = await followUps.GetOrderBookAsync(default);
        book.IsSuccess.Should().BeTrue();
        (book.Value!.HistoryMonths, book.Value.MinimumSample).Should().Be((24, 3));
    }
}
