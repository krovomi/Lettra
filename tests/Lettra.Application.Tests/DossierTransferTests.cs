using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Export et import d'un dossier : la phrase secrète est vérifiée avant tout, et seul l'administrateur du cabinet y a droit.</summary>
public sealed class DossierTransferTests
{
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly RecordingTransfer _transfer = new();

    [Theory]
    [InlineData("")]
    [InlineData("onze carac.")]
    [InlineData("            ")]
    public async Task Une_phrase_secrete_trop_courte_ou_vide_est_refusee_sans_rien_exporter_ni_importer(string passphrase)
    {
        (await new ExportDossierHandler(_transfer).HandleAsync(new ExportDossierCommand(Cabinet, Dossier, passphrase), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.PassphraseTooShortCode);
        using MemoryStream package = new();
        (await new ImportDossierHandler(_transfer).HandleAsync(new ImportDossierCommand(Cabinet, package, passphrase), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.PassphraseTooShortCode);
        _transfer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Une_phrase_secrete_trop_longue_est_refusee()
    {
        string passphrase = new('x', DossierPackageRules.MaxPassphraseLength + 1);
        (await new ExportDossierHandler(_transfer).HandleAsync(new ExportDossierCommand(Cabinet, Dossier, passphrase), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.PassphraseTooShortCode);
    }

    [Fact]
    public async Task Une_phrase_secrete_valide_passe_au_transfert_telle_que_saisie()
    {
        const string passphrase = "  cheval agrafe batterie  ";
        (await new ExportDossierHandler(_transfer).HandleAsync(new ExportDossierCommand(Cabinet, Dossier, passphrase), default)).IsSuccess.Should().BeTrue();
        using MemoryStream package = new();
        (await new ImportDossierHandler(_transfer).HandleAsync(new ImportDossierCommand(Cabinet, package, passphrase), default)).IsSuccess.Should().BeTrue();
        _transfer.Passphrases.Should().Equal(passphrase, passphrase);
    }

    [Fact]
    public async Task Un_poste_relie_a_un_serveur_n_exporte_ni_n_importe()
    {
        NoDossierTransfer none = new();
        (await none.ExportAsync(Cabinet, Dossier, "phrase", default)).Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.TransferUnavailableCode);
        using MemoryStream package = new();
        (await none.ImportAsync(Cabinet, package, "phrase", default)).Errors.Should().ContainSingle().Which.Code.Should().Be(DossierErrors.TransferUnavailableCode);
    }

    [Fact]
    public void L_export_et_l_import_sont_reserves_a_l_administrateur_du_cabinet()
    {
        new ExportDossierCommand(Cabinet, Dossier, "x").Permission.Should().Be(Abstractions.Identity.Permission.CabinetAdministrer);
        new ImportDossierCommand(Cabinet, Stream.Null, "x").Permission.Should().Be(Abstractions.Identity.Permission.CabinetAdministrer);
    }

    private sealed class RecordingTransfer : IDossierTransfer
    {
        public int Calls => Passphrases.Count;

        public List<string> Passphrases { get; } = [];

        public Task<Result<DossierPackage>> ExportAsync(OrganisationId organisation, DossierId dossier, string passphrase, CancellationToken cancellationToken)
        {
            Passphrases.Add(passphrase);
            return Task.FromResult<Result<DossierPackage>>(new DossierPackage(Stream.Null, "dossier" + DossierPackageRules.FileExtension));
        }

        public Task<Result<DossierSummary>> ImportAsync(OrganisationId organisation, Stream package, string passphrase, CancellationToken cancellationToken)
        {
            Passphrases.Add(passphrase);
            return Task.FromResult<Result<DossierSummary>>(new DossierSummary(Dossier.Value, "Dossier"));
        }
    }
}
