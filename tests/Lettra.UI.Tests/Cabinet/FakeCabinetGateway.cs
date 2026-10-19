using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Cabinet;

/// <summary>Trois dossiers du cabinet, leurs indicateurs et une revue en mémoire.</summary>
internal sealed class FakeCabinetGateway : ICabinetGateway
{
    public static readonly Guid Boulangerie = new("0199e3a0-0000-7000-8000-0000000b0001");
    public static readonly Guid Garage = new("0199e3a0-0000-7000-8000-0000000b0002");
    public static readonly Guid Atelier = new("0199e3a0-0000-7000-8000-0000000b0003");

    private readonly Dictionary<string, DossierReviewItemDto> _review = [];

    public List<DossierSummary> Dossiers { get; } =
    [
        new(Boulangerie, "Boulangerie Martin"),
        new(Garage, "Garage du Centre"),
        new(Atelier, "Atelier Éole"),
    ];

    public bool Computed { get; set; } = true;

    public int Refreshes { get; private set; }

    public List<(string Period, string Item, SetDossierReviewItemRequest Request)> Sets { get; } = [];

    public Task<OperationResult<IReadOnlyList<DossierSummary>>> ListDossiersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<DossierSummary>>.Success([.. Dossiers]));

    public Task<OperationResult<CabinetOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? at = Computed ? new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero) : null;
        IReadOnlyList<DossierIndicatorDto> None(params DossierIndicatorDto[] indicators) => Computed ? indicators : [];
        CabinetOverviewResponse response = new(
        [
            new(Garage, "Garage du Centre", at, Computed ? IndicatorLevelDto.Critical : IndicatorLevelDto.Ok, None(
                new DossierIndicatorDto(DossierIndicatorKeys.OverdueInvoices, 3, IndicatorLevelDto.Critical, new DateOnly(2026, 8, 31)),
                new DossierIndicatorDto(DossierIndicatorKeys.PlatformRejections, 1, IndicatorLevelDto.Critical, null),
                new DossierIndicatorDto("module.futur", 2, IndicatorLevelDto.Info, null))),
            new(Atelier, "Atelier Éole", at, Computed ? IndicatorLevelDto.Warning : IndicatorLevelDto.Ok, None(
                new DossierIndicatorDto(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevelDto.Ok, null),
                new DossierIndicatorDto(DossierIndicatorKeys.ReviewPending, 8, IndicatorLevelDto.Warning, null))),
            new(Boulangerie, "Boulangerie Martin", at, IndicatorLevelDto.Ok, None(
                new DossierIndicatorDto(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevelDto.Ok, null))),
        ]);
        return Task.FromResult(OperationResult<CabinetOverviewResponse>.Success(response));
    }

    public Task<OperationResult<CabinetOverviewRefreshResponse>> RefreshOverviewAsync(Guid? dossierId, CancellationToken cancellationToken)
    {
        Refreshes++;
        Computed = true;
        return Task.FromResult(OperationResult<CabinetOverviewRefreshResponse>.Success(new(Dossiers.Count, 0)));
    }

    public Task<OperationResult<DossierReviewResponse>> GetReviewAsync(string period, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<DossierReviewResponse>.Success(Review(period)));

    public Task<OperationResult<DossierReviewResponse>> SetReviewItemAsync(string period, string item, SetDossierReviewItemRequest request, CancellationToken cancellationToken)
    {
        Sets.Add((period, item, request));
        if (request.Checked)
        {
            _review[item] = new DossierReviewItemDto(item, true, Guid.Empty, "Claire Martin", true, DateTimeOffset.UtcNow, request.Note);
        }
        else
        {
            _review.Remove(item);
        }

        return Task.FromResult(OperationResult<DossierReviewResponse>.Success(Review(period)));
    }

    private DossierReviewResponse Review(string period)
    {
        string[] keys = ["pieces.emises", "pa.suivie", "relances.faites", "banque.rapprochee", "tva.declaree", "pieces.manquantes", "periode.cloturee", "points.manager"];
        List<DossierReviewItemDto> items = [.. keys.Select(k => _review.TryGetValue(k, out DossierReviewItemDto? done) ? done : new DossierReviewItemDto(k, false, null, null, false, null, null))];
        return new DossierReviewResponse(period, items.Count(i => i.Checked), items);
    }
}

internal static class CabinetFakesRegistration
{
    /// <summary>Le sélecteur de dossier de la barre du haut lit ses dossiers ; un écran qui le rend en a besoin.</summary>
    public static IServiceCollection AddCabinetFakes(this IServiceCollection services, FakeCabinetGateway? gateway = null)
    {
        services.AddSingleton<ICabinetGateway>(gateway ?? new FakeCabinetGateway());
        services.AddSingleton<IDossierSelection>(new FixedDossierSelection(FakeCabinetGateway.Boulangerie));
        return services;
    }
}
