using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Identity.Oidc.Offline;
using Microsoft.Extensions.Options;

namespace Lettra.Identity.Tests;

/// <summary>Instantané de droits signé pour le hors ligne (annexe F §4) : signature, poste, échéance, rotation de clé.</summary>
public sealed class RightsSnapshotTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Device = Guid.CreateVersion7();
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly EcdsaRightsSnapshotIssuer _issuer = Issuer("cle-1");

    public void Dispose() => _issuer.Dispose();

    private static EcdsaRightsSnapshotIssuer Issuer(string keyId, string? pem = null, params RightsSnapshotPublicKeyOptions[] previous)
    {
        RightsSnapshotOptions options = new() { KeyId = keyId, SigningKeyPem = pem, AllowEphemeralKey = pem is null };
        foreach (RightsSnapshotPublicKeyOptions key in previous)
        {
            options.PreviousKeys.Add(key);
        }

        return new EcdsaRightsSnapshotIssuer(Options.Create(options));
    }

    private static RightsSnapshot Snapshot(Guid? device = null, DateTimeOffset? issuedAt = null) => new(
        User,
        device ?? Device,
        issuedAt ?? Now,
        (issuedAt ?? Now).AddDays(7),
        new Dictionary<DossierId, IReadOnlySet<Permission>>
        {
            [Dossier] = new HashSet<Permission> { Permission.TiersConsulter, Permission.FactureSaisir, Permission.DossierLire },
        });

    private static RightsSnapshotVerifier VerifierFor(EcdsaRightsSnapshotIssuer issuer) =>
        new(RightsSnapshotKeys.FromJwks(issuer.PublicKeys()));

    [Fact]
    public void Un_instantane_signe_se_verifie_hors_ligne_avec_les_cles_publiees()
    {
        string token = _issuer.Sign(Snapshot());

        Result<RightsSnapshot> verified = VerifierFor(_issuer).Verify(token, Device, User, Now.AddDays(3));

        verified.IsSuccess.Should().BeTrue();
        verified.Value.Dossiers[Dossier].Should().BeEquivalentTo([Permission.TiersConsulter, Permission.FactureSaisir, Permission.DossierLire]);
        verified.Value.ExpiresAt.Should().Be(Now.AddDays(7));
    }

    [Fact]
    public void Un_instantane_modifie_ne_donne_aucun_droit()
    {
        string token = _issuer.Sign(Snapshot());
        string[] parts = token.Split('.');
        string forged = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(parts[1])).Replace("[1,", "[1,72,", StringComparison.Ordinal)));

        Result<RightsSnapshot> verified = VerifierFor(_issuer).Verify($"{parts[0]}.{forged}.{parts[2]}", Device, User, Now);

        verified.Errors.Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.InvalidCode);
    }

    [Theory]
    [InlineData("""{"alg":"none","typ":"lettra-rights+jws","kid":"cle-1"}""")]
    [InlineData("""{"alg":"HS256","typ":"lettra-rights+jws","kid":"cle-1"}""")]
    [InlineData("""{"alg":"ES256","typ":"JWT","kid":"cle-1"}""")]
    [InlineData("""{"alg":"ES256","typ":"lettra-rights+jws","kid":"inconnue"}""")]
    public void Seuls_ES256_le_type_attendu_et_une_cle_publiee_sont_acceptes(string header)
    {
        string[] parts = _issuer.Sign(Snapshot()).Split('.');
        string token = $"{System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header))}.{parts[1]}.{parts[2]}";

        VerifierFor(_issuer).Verify(token, Device, User, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.InvalidCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-un-jws")]
    [InlineData("a.b.c")]
    public void Un_jeton_mal_forme_est_invalide(string token) =>
        VerifierFor(_issuer).Verify(token, Device, User, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.InvalidCode);

    [Fact]
    public void Un_instantane_signe_par_une_autre_cle_est_invalide()
    {
        using EcdsaRightsSnapshotIssuer other = Issuer("cle-1");

        VerifierFor(_issuer).Verify(other.Sign(Snapshot()), Device, User, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.InvalidCode);
    }

    [Fact]
    public void Un_instantane_copie_sur_un_autre_poste_est_refuse() =>
        VerifierFor(_issuer).Verify(_issuer.Sign(Snapshot()), Guid.CreateVersion7(), User, Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.OtherDeviceCode);

    [Fact]
    public void Un_instantane_d_un_autre_utilisateur_est_invalide() =>
        VerifierFor(_issuer).Verify(_issuer.Sign(Snapshot()), Device, Guid.CreateVersion7(), Now).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.InvalidCode);

    [Fact]
    public void Apres_l_echeance_l_instantane_est_expire() =>
        VerifierFor(_issuer).Verify(_issuer.Sign(Snapshot()), Device, User, Now.AddDays(7)).Errors
            .Should().ContainSingle().Which.Code.Should().Be(RightsSnapshotErrors.ExpiredCode);

    [Fact]
    public void Apres_une_rotation_l_ancienne_cle_reste_acceptee_le_temps_de_l_echeance()
    {
        using ECDsa oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaRightsSnapshotIssuer before = Issuer("cle-1", oldKey.ExportPkcs8PrivateKeyPem());
        string oldToken = before.Sign(Snapshot());

        using EcdsaRightsSnapshotIssuer after = Issuer(
            "cle-2", newKey.ExportPkcs8PrivateKeyPem(), new RightsSnapshotPublicKeyOptions { KeyId = "cle-1", PublicKeyPem = oldKey.ExportSubjectPublicKeyInfoPem() });

        after.PublicKeys().Keys.Select(k => k.Kid).Should().BeEquivalentTo(["cle-1", "cle-2"]);
        VerifierFor(after).Verify(oldToken, Device, User, Now).IsSuccess.Should().BeTrue();
        VerifierFor(after).Verify(after.Sign(Snapshot()), Device, User, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Sans_cle_configuree_le_hors_ligne_est_indisponible()
    {
        using EcdsaRightsSnapshotIssuer issuer = new(Options.Create(new RightsSnapshotOptions()));

        issuer.IsAvailable.Should().BeFalse();
        issuer.PublicKeys().Keys.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(24 * 31)]
    public void La_validite_est_bornee_entre_une_heure_et_trente_jours(double hours)
    {
        Action create = () =>
        {
            using EcdsaRightsSnapshotIssuer issuer = new(Options.Create(new RightsSnapshotOptions
            {
                AllowEphemeralKey = true,
                Validity = TimeSpan.FromHours(hours),
            }));
        };

        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Hors_ligne_les_permissions_sont_celles_de_l_instantane_jusqu_a_son_echeance()
    {
        TestClock clock = new(Now);
        RightsSnapshot snapshot = Snapshot();
        OfflinePermissionService service = new(() => snapshot, clock);

        Granted(service, User, AuthorizationScope.ForDossier(Dossier)).Should().Contain(Permission.FactureSaisir);
        Granted(service, Guid.CreateVersion7(), AuthorizationScope.ForDossier(Dossier)).Should().BeEmpty();
        Granted(service, User, AuthorizationScope.ForDossier(new DossierId(Guid.CreateVersion7()))).Should().BeEmpty();
        Granted(service, User, new AuthorizationScope(ScopeKind.Cabinet, Guid.CreateVersion7())).Should().BeEmpty();

        clock.Advance(TimeSpan.FromDays(7));
        Granted(service, User, AuthorizationScope.ForDossier(Dossier)).Should().BeEmpty("le poste se verrouille à l'échéance");
    }

    [Fact]
    public void Sans_instantane_rien_n_est_permis_hors_ligne() =>
        Granted(new OfflinePermissionService(() => null, new TestClock(Now)), User, AuthorizationScope.ForDossier(Dossier)).Should().BeEmpty();

    private static IReadOnlySet<Permission> Granted(OfflinePermissionService service, Guid user, AuthorizationScope scope) =>
        service.GetEffectiveAsync(user, scope, CancellationToken.None).AsTask().GetAwaiter().GetResult();
}

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delay) => _now += delay;
}
