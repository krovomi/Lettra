using System.Text;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Pièces justificatives des dépenses par la même interface, en HTTP et en direct : mêmes résultats, mêmes refus.</summary>
public sealed class ReceiptsGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    /// <summary>Le plus petit PNG valable : un pixel.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static async Task ImportAsync(IServiceProvider services)
    {
        ImportBankStatementRequest file = new()
        {
            FileName = "releve.xml",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BankingApiTests.Camt(Today, 120m, "VIR SEPA MARTIN"))),
        };
        (await services.GetRequiredService<IBankingGateway>().ImportStatementAsync(file, default)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Reclamer_deposer_telecharger_puis_dire_non_attendue(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IReceiptsGateway receipts = services.GetRequiredService<IReceiptsGateway>();
        await ImportAsync(services);

        ReceiptsResponse before = (await receipts.GetReceiptsAsync(default)).Value!;
        ReceiptLineResponse fee = before.Lines.Should().ContainSingle().Subject;
        OperationResult<RequestReceiptsResponse> requested = await receipts.RequestAsync(
            new RequestReceiptsRequest { LineIds = [fee.LineId], Email = "compta@martin.example", Note = "Pour la clôture" }, default);
        ReceiptsResponse toProvide = (await receipts.GetRequestedAsync(default)).Value!;
        OperationResult<ReceiptLineResponse> uploaded = await receipts.UploadAsync(
            fee.LineId, new UploadReceiptRequest { FileName = "ticket.png", ContentBase64 = Convert.ToBase64String(Png) }, default);
        OperationResult<ReceiptDocument> downloaded = await receipts.DownloadAsync(fee.LineId, default);
        ReceiptsResponse after = (await receipts.GetReceiptsAsync(default)).Value!;
        OperationResult<RequestReceiptsResponse> again = await receipts.RequestAsync(
            new RequestReceiptsRequest { LineIds = [fee.LineId], Email = "compta@martin.example" }, default);

        before.MissingCount.Should().Be(1);
        fee.Amount.Should().Be(8.50m, "une dépense se montre en positif");
        fee.Status.Should().Be(ReceiptStatusDto.Missing);
        requested.IsSuccess.Should().BeTrue(GatewayHarness.Codes(requested));
        requested.Value!.Requested.Should().Be(1);
        requested.Value.Email.Should().Be(InvitationEmailStatusDto.NotConfigured, "les tests n'envoient pas d'e-mails");
        toProvide.Lines.Should().ContainSingle().Which.RequestedTo.Should().Be("compta@martin.example");
        uploaded.Value!.Status.Should().Be(ReceiptStatusDto.Received);
        uploaded.Value.Receipt!.ContentType.Should().Be("image/png");
        downloaded.Value!.Content.Should().Equal(Png);
        downloaded.Value.FileName.Should().Be("ticket.png");
        after.ReceivedCount.Should().Be(1);
        after.MissingCount.Should().Be(0);
        after.LastRecipient.Should().Be("compta@martin.example");
        (await receipts.GetRequestedAsync(default)).Value!.Lines.Should().BeEmpty();
        again.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Receipt.AlreadyReceived);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Une_piece_non_attendue_puis_de_nouveau_attendue(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IReceiptsGateway receipts = services.GetRequiredService<IReceiptsGateway>();
        await ImportAsync(services);
        Guid fee = (await receipts.GetReceiptsAsync(default)).Value!.Lines.Single().LineId;

        OperationResult<ReceiptLineResponse> notNeeded = await receipts.SetNotNeededAsync(fee, notNeeded: true, default);
        ReceiptsResponse list = (await receipts.GetReceiptsAsync(default)).Value!;
        OperationResult<ReceiptLineResponse> needed = await receipts.SetNotNeededAsync(fee, notNeeded: false, default);

        notNeeded.Value!.Status.Should().Be(ReceiptStatusDto.NotNeeded);
        list.MissingCount.Should().Be(0);
        needed.Value!.Status.Should().Be(ReceiptStatusDto.Missing);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IReceiptsGateway receipts = services.GetRequiredService<IReceiptsGateway>();
        await ImportAsync(services);
        Guid fee = (await receipts.GetReceiptsAsync(default)).Value!.Lines.Single().LineId;
        Guid credit = (await services.GetRequiredService<IBankingGateway>().GetReconciliationAsync(default)).Value!.Lines.Single(l => l.Amount > 0).Id;

        OperationResult<RequestReceiptsResponse> none = await receipts.RequestAsync(new RequestReceiptsRequest { LineIds = [], Email = "a@b.example" }, default);
        OperationResult<RequestReceiptsResponse> badEmail = await receipts.RequestAsync(new RequestReceiptsRequest { LineIds = [fee], Email = "pas une adresse" }, default);
        OperationResult<RequestReceiptsResponse> unknown = await receipts.RequestAsync(new RequestReceiptsRequest { LineIds = [Guid.CreateVersion7()], Email = "a@b.example" }, default);
        OperationResult<RequestReceiptsResponse> onCredit = await receipts.RequestAsync(new RequestReceiptsRequest { LineIds = [credit], Email = "a@b.example" }, default);
        OperationResult<ReceiptLineResponse> notAPicture = await receipts.UploadAsync(
            fee, new UploadReceiptRequest { FileName = "virus.exe", ContentBase64 = Convert.ToBase64String("MZ pas une photo"u8.ToArray()) }, default);
        OperationResult<ReceiptLineResponse> empty = await receipts.UploadAsync(fee, new UploadReceiptRequest { FileName = "vide.png" }, default);
        OperationResult<ReceiptDocument> nothing = await receipts.DownloadAsync(fee, default);
        OperationResult<ReceiptLineResponse> unknownLine = await receipts.SetNotNeededAsync(Guid.CreateVersion7(), notNeeded: true, default);

        none.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Receipt.LinesRequired);
        badEmail.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Receipt.EmailInvalid);
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankStatement.LineNotFound);
        onCredit.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Receipt.LineNotDebit);
        notAPicture.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Receipt.FileTypeInvalid);
        empty.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Receipt.FileRequired);
        nothing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Receipt.NotFound);
        unknownLine.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankStatement.LineNotFound);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IReceiptsGateway receipts = services.GetRequiredService<IReceiptsGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await receipts.GetReceiptsAsync(default)).Errors,
            (await receipts.GetRequestedAsync(default)).Errors,
            (await receipts.RequestAsync(new RequestReceiptsRequest { LineIds = [id], Email = "a@b.example" }, default)).Errors,
            (await receipts.SetNotNeededAsync(id, notNeeded: true, default)).Errors,
            (await receipts.UploadAsync(id, new UploadReceiptRequest(), default)).Errors,
            (await receipts.DownloadAsync(id, default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
