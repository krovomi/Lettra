using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Exports.Application;
using Lettra.Exports.Application.Bridges;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Tests;

public sealed class FecExportServiceTests
{
    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: null, LatePenaltyRate: 10m, EarlyPaymentDiscount: null, Iban: null, Bic: null);

    private static FecExportService Service(CompanyProfile? profile) => new(new Profiles(profile), new Journal());

    private static CompanyProfile Profile(CompanyProfile.Draft draft) =>
        CompanyProfile.Create(draft, DateTimeOffset.UtcNow).Value;

    [Fact]
    public async Task Le_fec_porte_le_siren_et_la_date_de_cloture()
    {
        Result<FecFile> file = await Service(Profile(Cabinet)).ExportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default);

        file.IsSuccess.Should().BeTrue();
        file.Value.FileName.Should().Be("552100554FEC20261231.txt");
        file.Value.Validation.IsValid.Should().BeTrue();
        file.Value.Validation.EntryCount.Should().Be(4);
    }

    [Fact]
    public async Task Les_ecritures_partent_au_format_du_logiciel_du_cabinet()
    {
        Result<BridgeFile> file = await Service(Profile(Cabinet))
            .ExportForAsync(AccountingSoftwareDto.Quadra, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default);

        file.IsSuccess.Should().BeTrue();
        file.Value.FileName.Should().Be("552100554-quadra-20260101-20261231.txt");
    }

    [Fact]
    public async Task Une_passerelle_inconnue_est_refusee() =>
        (await Service(Profile(Cabinet)).ExportForAsync((AccountingSoftwareDto)42, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default))
            .Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Fec.SoftwareUnknown);

    [Fact]
    public async Task Une_passerelle_suit_les_refus_du_fec()
    {
        (await Service(null).ExportForAsync(AccountingSoftwareDto.Sage, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default))
            .Errors.Select(e => e.Code).Should().Equal(CompanyProfileErrors.NotFoundCode);
        (await Service(Profile(Cabinet)).ExportForAsync(AccountingSoftwareDto.Sage, new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), default))
            .Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Fec.PeriodInvalid);
    }

    [Fact]
    public async Task Sans_fiche_entreprise_l_export_est_refuse() =>
        (await Service(null).ExportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default))
            .Errors.Select(e => e.Code).Should().Equal(CompanyProfileErrors.NotFoundCode);

    [Fact]
    public async Task Sans_siren_l_export_est_refuse() =>
        (await Service(Profile(Cabinet with { Siren = null, Siret = null, Rcs = null, VatNumber = null })).ExportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default))
            .Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Fec.SirenMissing);

    [Theory]
    [InlineData("2026-12-31", "2026-01-01", ErrorCodes.Fec.PeriodInvalid)]
    [InlineData("2025-01-01", "2027-01-01", ErrorCodes.Fec.PeriodTooLong)]
    public async Task Un_exercice_incoherent_est_refuse(string from, string to, string code) =>
        (await Service(Profile(Cabinet)).ExportAsync(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), default))
            .Errors.Select(e => e.Code).Should().Equal(code);

    [Fact]
    public void Un_premier_exercice_de_24_mois_est_accepte() =>
        FecExportService.ValidatePeriod(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31)).IsSuccess.Should().BeTrue();

    private sealed class Profiles(CompanyProfile? profile) : ICompanyProfileRepository
    {
        public Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Journal : ISalesJournalReader
    {
        public Task<IReadOnlyList<SalesJournalExportEntry>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesJournalExportEntry>>([.. FecSamples.Year2026.Where(e => e.Date >= from && e.Date <= to)]);
    }
}
