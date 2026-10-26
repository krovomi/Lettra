using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Attestations;

/// <summary>Fournisseurs fictifs : la passerelle garde les lignes et refait l'état comme le serveur (seuil, six mois, vérification).</summary>
internal sealed class FakeAttestationsGateway(DateOnly today) : IAttestationsGateway
{
    public List<SupplierAttestationRow> Rows { get; } = [];

    public List<(Guid SupplierId, SaveAttestationRequest Request, long? Version)> Saves { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextErrors { get; set; }

    public SupplierAttestationRow Add(string name, decimal? amount = null, DateOnly? issued = null, bool verified = false)
    {
        SupplierAttestationRow row = Row(Guid.CreateVersion7(), name, amount, issued, issued is null ? null : "AB12CD34EF56", verified ? issued : null, amount is null ? null : 3);
        Rows.Add(row);
        return row;
    }

    public Task<OperationResult<SupplierAttestationsResponse>> ListAsync(CancellationToken cancellationToken) => Answer(() => { });

    public Task<OperationResult<SupplierAttestationsResponse>> SaveAsync(Guid supplierId, SaveAttestationRequest request, long? expectedVersion, CancellationToken cancellationToken) =>
        Answer(() =>
        {
            Saves.Add((supplierId, request, expectedVersion));
            int index = Rows.FindIndex(r => r.SupplierId == supplierId);
            string? code = string.IsNullOrWhiteSpace(request.SecurityCode) ? null : request.SecurityCode.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            // Comme la base : le montant revient avec une décimale (« 18000.0 »).
            Rows[index] = Row(supplierId, Rows[index].SupplierName, request.ContractAmountExclTax + 0.0m, request.IssuedOn, code, request.VerifiedOn, (expectedVersion ?? 0) + 1);
        });

    private SupplierAttestationRow Row(Guid id, string name, decimal? amount, DateOnly? issued, string? code, DateOnly? verified, long? version)
    {
        DateOnly? until = issued?.AddMonths(6).AddDays(-1);
        AttestationStatusDto status = amount is not >= 5_000m ? AttestationStatusDto.NotRequired
            : until is null ? AttestationStatusDto.Missing
            : until < today ? AttestationStatusDto.Expired
            : verified is null ? AttestationStatusDto.NotVerified
            : until.Value.DayNumber - today.DayNumber < 30 ? AttestationStatusDto.ExpiringSoon
            : AttestationStatusDto.Valid;
        return new SupplierAttestationRow(id, name, amount, issued, until, code, verified, status, version);
    }

    private Task<OperationResult<SupplierAttestationsResponse>> Answer(Action change)
    {
        if (NextErrors is { } errors)
        {
            NextErrors = null;
            return Task.FromResult(OperationResult<SupplierAttestationsResponse>.Failure(errors));
        }

        change();
        return Task.FromResult(OperationResult<SupplierAttestationsResponse>.Success(new SupplierAttestationsResponse([.. Rows])));
    }
}
