using System.Net;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Changements d'IBAN des fournisseurs par la même interface, en HTTP et en direct : un nouvel IBAN saisi sur la fiche
/// attend sa vérification, se confirme ou se refuse une seule fois, et le plus récent remplace celui qui attendait.
/// </summary>
public sealed class IbanChangeGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private const string OtherFrenchIban = "FR14 2004 1010 0505 0001 3M02 606";
    private const string GermanIban = "DE89370400440532013000";

    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static async Task<(SupplierResponse Supplier, CreateSupplierRequest Request)> SupplierAsync(ISuppliersGateway suppliers)
    {
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = "Minoterie " + Guid.CreateVersion7().ToString("N")[^8..];
        return ((await suppliers.CreateAsync(request, default)).Value!, request);
    }

    private static async Task<SupplierResponse> ChangeIbanAsync(ISuppliersGateway suppliers, SupplierResponse supplier, CreateSupplierRequest request, string iban)
    {
        request.Iban = iban;
        return (await suppliers.UpdateAsync(supplier.Id, supplier.Version, request, default)).Value!;
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_nouvel_iban_attend_sa_verification_puis_se_confirme_une_fois(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        IIbanChangesGateway changes = services.GetRequiredService<IIbanChangesGateway>();
        (SupplierResponse created, CreateSupplierRequest request) = await SupplierAsync(suppliers);

        created.PendingIbanChange.Should().BeNull("un IBAN saisi à la création n'est pas un changement");
        SupplierResponse changed = await ChangeIbanAsync(suppliers, created, request, OtherFrenchIban);

        IbanChangeResponse pending = changed.PendingIbanChange!;
        pending.PreviousIban.Should().Be("FR76 •••• 0189");
        pending.NewIban.Should().Be("FR14 •••• 2606");
        pending.SupplierName.Should().Be(request.Name);
        pending.Status.Should().Be(IbanChangeStatusDto.Pending);
        (await changes.ListAsync(new IbanChangeListQuery(Search: request.Name), default)).Value!.Items.Should().ContainSingle().Which.Id.Should().Be(pending.Id);

        OperationResult<bool> withoutMethod = await changes.ConfirmAsync(pending.Id, new ConfirmIbanChangeRequest(), default);
        OperationResult<bool> confirmed = await changes.ConfirmAsync(
            pending.Id, new ConfirmIbanChangeRequest { Method = IbanVerificationMethodDto.KnownPhoneNumber, Note = "Appel au standard", ExpectedVersion = pending.Version }, default);
        OperationResult<bool> again = await changes.RejectAsync(pending.Id, new RejectIbanChangeRequest { Note = "Trop tard" }, default);

        withoutMethod.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.IbanChange.MethodRequired);
        confirmed.Value.Should().BeTrue();
        again.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.IbanChange.AlreadyDecided);
        (await suppliers.GetAsync(created.Id, default)).Value!.PendingIbanChange.Should().BeNull();
        (await changes.ListAsync(new IbanChangeListQuery(Search: request.Name), default)).Value!.Items.Should().BeEmpty("seuls les changements à vérifier par défaut");
        IbanChangeResponse history = (await changes.ForSupplierAsync(created.Id, default)).Value!.Should().ContainSingle().Subject;
        history.Status.Should().Be(IbanChangeStatusDto.Confirmed);
        history.Method.Should().Be(IbanVerificationMethodDto.KnownPhoneNumber);
        history.Note.Should().Be("Appel au standard");
        history.DecidedAt.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_changement_le_plus_recent_remplace_celui_qui_attendait_et_se_refuse_avec_une_note(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        IIbanChangesGateway changes = services.GetRequiredService<IIbanChangesGateway>();
        (SupplierResponse created, CreateSupplierRequest request) = await SupplierAsync(suppliers);

        SupplierResponse first = await ChangeIbanAsync(suppliers, created, request, OtherFrenchIban);
        SupplierResponse second = await ChangeIbanAsync(suppliers, first, request, GermanIban);

        second.PendingIbanChange!.CountryChanged.Should().BeTrue();
        IReadOnlyList<IbanChangeResponse> history = (await changes.ForSupplierAsync(created.Id, default)).Value!;
        history.Select(c => c.Status).Should().Equal(IbanChangeStatusDto.Pending, IbanChangeStatusDto.Superseded);
        PagedResponse<IbanChangeResponse> all = (await changes.ListAsync(new IbanChangeListQuery(null, request.Name, Sort: IbanChangeSortDto.Supplier), default)).Value!;
        all.TotalCount.Should().Be(2);

        OperationResult<bool> withoutNote = await changes.RejectAsync(second.PendingIbanChange.Id, new RejectIbanChangeRequest(), default);
        OperationResult<bool> rejected = await changes.RejectAsync(second.PendingIbanChange.Id, new RejectIbanChangeRequest { Note = "Le fournisseur n'a rien demandé" }, default);
        OperationResult<bool> unknown = await changes.ConfirmAsync(Guid.CreateVersion7(), new ConfirmIbanChangeRequest { Method = IbanVerificationMethodDto.InPerson }, default);

        withoutNote.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.IbanChange.NoteRequired);
        rejected.Value.Should().BeTrue();
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.IbanChange.NotFound);
        (await changes.ListAsync(new IbanChangeListQuery(IbanChangeStatusDto.Rejected, request.Name), default)).Value!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Decider_exige_le_droit_de_modifier_les_tiers()
    {
        using HttpClient reader = factory.ClientFor(LettraApiFactory.PortailId);
        reader.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        using HttpResponseMessage response = await reader.PostAsync(
            new Uri($"/api/v1/suppliers/iban-changes/{Guid.CreateVersion7()}/rejection", UriKind.Relative),
            new StringContent("{\"note\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _harness.Dispose();
}
