using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Portal;

/// <summary>Lien d'espace client et preuve de signature d'un devis.</summary>
public sealed class PortalLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly CustomerId Client = new(Guid.CreateVersion7());
    private static readonly QuoteId Devis = new(Guid.CreateVersion7());
    private static readonly string Hash = new('a', PortalLink.SecretHashLength);

    [Fact]
    public void Un_lien_vit_le_nombre_de_jours_demande_puis_expire()
    {
        Guid user = Guid.CreateVersion7();
        PortalLink link = PortalLink.Create(Client, Hash, Devis, 30m, PortalLink.DefaultValidityDays, user, Now).Value;

        link.Should().BeEquivalentTo(new { CustomerId = Client, SecretHash = Hash, QuoteId = (QuoteId?)Devis, DownPaymentPercent = (decimal?)30m, CreatedBy = user, CreatedAt = Now });
        link.ExpiresAt.Should().Be(Now.AddDays(30));
        link.IsActive(Now.AddDays(29)).Should().BeTrue();
        link.IsActive(Now.AddDays(30)).Should().BeFalse();
        link.LastOpenedAt.Should().BeNull();

        link.Opened(Now.AddHours(1));
        link.LastOpenedAt.Should().Be(Now.AddHours(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void Une_duree_hors_bornes_est_refusee(int days)
    {
        PortalLink.Create(Client, Hash, null, null, days, Guid.Empty, Now).Errors[0].Code.Should().Be(PortalErrors.ValidityInvalidCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.5)]
    [InlineData(33.333)]
    public void Un_acompte_hors_bornes_est_refuse(double percent)
    {
        PortalLink.Create(Client, Hash, Devis, (decimal)percent, 30, Guid.Empty, Now).Errors[0].Code.Should().Be(PortalErrors.DownPaymentInvalidCode);
    }

    [Fact]
    public void Un_acompte_ne_se_demande_qu_avec_un_devis()
    {
        PortalLink.Create(Client, Hash, null, 30m, 30, Guid.Empty, Now).Errors[0].Code.Should().Be(PortalErrors.DownPaymentInvalidCode);
        PortalLink.Create(Client, Hash, Devis, 100m, 30, Guid.Empty, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_lien_revoque_ne_marche_plus_et_ne_se_revoque_qu_une_fois()
    {
        PortalLink link = PortalLink.Create(Client, Hash, null, null, 30, Guid.Empty, Now).Value;
        Guid user = Guid.CreateVersion7();

        link.Revoke(user, Now.AddDays(1)).IsSuccess.Should().BeTrue();

        link.IsActive(Now.AddDays(2)).Should().BeFalse();
        link.RevokedBy.Should().Be(user);
        link.RevokedAt.Should().Be(Now.AddDays(1));
        link.Revoke(user, Now.AddDays(3)).Errors[0].Code.Should().Be(PortalErrors.LinkRevokedCode);
    }

    [Fact]
    public void La_preuve_garde_le_nom_nettoye_et_borne_les_references_du_prestataire()
    {
        Guid link = Guid.CreateVersion7();
        QuoteSignature signature = QuoteSignature.Record(Devis, link, "  Marie \t Dupont ", Hash, new string('p', 50), new string('r', 150), Now).Value;

        signature.Should().BeEquivalentTo(new { QuoteId = Devis, PortalLinkId = link, SignerName = "Marie Dupont", SignedAt = Now, DocumentHash = Hash });
        signature.Provider.Should().HaveLength(QuoteSignature.MaxProviderLength);
        signature.ProviderReference.Should().HaveLength(QuoteSignature.MaxReferenceLength);
        signature.DownPaymentInvoiceId.Should().BeNull();

        InvoiceId invoice = new(Guid.CreateVersion7());
        signature.AttachDownPayment(invoice);
        signature.DownPaymentInvoiceId.Should().Be(invoice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("M")]
    public void Un_nom_de_signataire_trop_court_est_refuse(string? name)
    {
        QuoteSignature.SignerNameOf(name).Errors[0].Code.Should().Be(PortalErrors.SignerNameInvalidCode);
        QuoteSignature.Record(Devis, Guid.Empty, name!, Hash, "p", "r", Now).Errors[0].Code.Should().Be(PortalErrors.SignerNameInvalidCode);
    }

    [Fact]
    public void Un_nom_de_signataire_trop_long_est_refuse()
    {
        QuoteSignature.SignerNameOf(new string('x', QuoteSignature.MaxSignerNameLength)).IsSuccess.Should().BeTrue();
        QuoteSignature.SignerNameOf(new string('x', QuoteSignature.MaxSignerNameLength + 1)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Chaque_erreur_porte_son_code_et_sa_nature()
    {
        Error[] errors =
        [
            PortalErrors.Disabled, PortalErrors.LinkNotFound, PortalErrors.LinkRevoked, PortalErrors.ValidityInvalid, PortalErrors.DownPaymentInvalid,
            PortalErrors.QuoteNotSignable, PortalErrors.SignerNameInvalid, PortalErrors.ConsentRequired, PortalErrors.AlreadySigned,
            PortalErrors.NothingToPay, PortalErrors.PaymentDeclined, PortalErrors.CheckoutNotFound, PortalErrors.SignatureNotFound,
        ];

        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems().And.OnlyContain(c => c.StartsWith("Portal.", StringComparison.Ordinal));
        PortalErrors.Disabled.Kind.Should().Be(ErrorKind.Forbidden);
        PortalErrors.CheckoutNotFound.Kind.Should().Be(ErrorKind.NotFound);
        PortalErrors.ConsentRequired.Target.Should().Be("Consent");
    }
}
