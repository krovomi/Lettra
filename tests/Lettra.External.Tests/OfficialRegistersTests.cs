using Lettra.Application;
using Lettra.Application.Abstractions.External;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;
using Lettra.Infrastructure.External;
using Lettra.Infrastructure.External.Registers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.External.Tests;

/// <summary>Registres officiels simulés : des réponses fictives, toujours les mêmes pour un même SIREN.</summary>
public sealed class OfficialRegistersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly SimulatedOfficialRegisters _registers = new(new FixedClock(Now));

    private static Siren S(string value) => Siren.Create(value).Value;

    [Fact]
    public async Task Les_beneficiaires_dependent_du_siren_et_detiennent_le_capital()
    {
        IReadOnlyList<RegisteredOwner> owners = (await _registers.FindAsync(S("123456709"), default)).Value;

        owners.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(3);
        owners.Should().OnlyContain(o => o.Kind == OwnershipKind.Capital && o.SharePercent > 25m);
        owners.Sum(o => o.SharePercent).Should().Be(100m);
        owners.Should().NotContain(o => o.Name == SimulatedOfficialRegisters.FrozenOwner);
        (await _registers.FindAsync(S("123456709"), default)).Value.Should().Equal(owners, "même SIREN, même réponse");
    }

    [Fact]
    public async Task Un_siren_sur_dix_sept_a_un_beneficiaire_inscrit_au_registre_des_gels()
    {
        IReadOnlyList<RegisteredOwner> owners = (await _registers.FindAsync(S("123456774"), default)).Value;
        IReadOnlyList<FrozenEntry> frozen = (await _registers.ListAsync(default)).Value;

        owners[^1].Name.Should().Be(SimulatedOfficialRegisters.FrozenOwner);
        AssetFreezeNames.FirstMatch(owners.Select(o => o.Name), frozen.SelectMany(e => e.Aliases.Prepend(e.Name))).Should().Be(SimulatedOfficialRegisters.FrozenOwner);
        frozen.Should().HaveCount(4);
    }

    [Fact]
    public async Task Le_bodacc_simule_publie_des_procedures_et_des_radiations_datees_du_passe()
    {
        LegalNotice proceeding = (await _registers.ListAsync(S("123456733"), default)).Value.Should().ContainSingle().Subject;
        proceeding.Kind.Should().Be(LegalNoticeKind.CollectiveProceeding);
        proceeding.PublishedOn.Should().BeBefore(new DateOnly(2026, 10, 9)).And.BeAfter(new DateOnly(2026, 8, 9));

        (await _registers.ListAsync(S("123456725"), default)).Value.Should().ContainSingle().Which.Kind.Should().Be(LegalNoticeKind.StruckOff);
        (await _registers.ListAsync(S("123456709"), default)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task L_urssaf_simulee_reconnait_tout_code_sauf_ceux_en_ZZ()
    {
        (await _registers.VerifyAsync(S("123456709"), "AB12CD34EF56", default)).Value.Should().Be(UrssafAnswer.Recognized);
        (await _registers.VerifyAsync(S("123456709"), "zz12CD34EF56", default)).Value.Should().Be(UrssafAnswer.NotRecognized);
        await FluentActions.Awaiting(() => _registers.VerifyAsync(S("123456709"), null!, default)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => _registers.FindAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => _registers.ListAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Sans_les_services_externes_les_registres_disent_indisponible_avec_les_registres_simules()
    {
        ServiceCollection bare = new();
        bare.AddApplication();
        using ServiceProvider none = bare.BuildServiceProvider();
        (await none.GetRequiredService<IUrssafAttestationCheck>().VerifyAsync(S("123456709"), "AB12CD34EF56", default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.UnavailableCode);
        (await none.GetRequiredService<IAssetFreezeRegister>().ListAsync(default)).IsFailure.Should().BeTrue();
        (await none.GetRequiredService<IBeneficialOwnerRegister>().FindAsync(S("123456709"), default)).IsFailure.Should().BeTrue();
        (await none.GetRequiredService<ILegalNoticeFeed>().ListAsync(S("123456709"), default)).IsFailure.Should().BeTrue();

        ServiceCollection services = new();
        services.AddApplication();
        services.AddExternalServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:External:Enabled"] = "false" }).Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBeneficialOwnerRegister>().Should().BeOfType<SimulatedOfficialRegisters>();
        provider.GetRequiredService<IAssetFreezeRegister>().Should().BeSameAs(provider.GetRequiredService<ILegalNoticeFeed>());
        provider.GetRequiredService<IUrssafAttestationCheck>().Should().BeOfType<SimulatedOfficialRegisters>();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
