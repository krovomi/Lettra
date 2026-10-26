using Lettra.Domain.Banking;

namespace Lettra.Domain.Tests.Banking;

/// <summary>Banque connectée : consentement, échéance et renouvellement, réservation de la synchronisation, déconnexion.</summary>
public sealed class BankConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static BankConnection Pending(Guid? replaces = null) =>
        BankConnection.Start("Agrégateur simulé", "sim-lumiere", "Banque Lumière (simulée)", "sim-ref", replaces, User, Now, Guid.CreateVersion7());

    private static BankConnection Active()
    {
        BankConnection connection = Pending();
        connection.Activate("FR76 3000 4000 0312 3456 7890 143", Now.AddDays(180), Now).IsSuccess.Should().BeTrue();
        return connection;
    }

    [Fact]
    public void Un_consentement_demande_n_est_pas_encore_actif()
    {
        Guid id = Guid.CreateVersion7();
        BankConnection connection = BankConnection.Start(" Agrégateur ", "sim-lumiere", "Banque", "ref", null, User, Now, id);

        connection.Should().BeEquivalentTo(new { Id = id, Provider = "Agrégateur", Status = BankConnectionStatus.Pending, CreatedBy = User, CreatedAt = Now, Version = 1L });
        connection.IsActive(Now).Should().BeFalse();
        connection.IsSyncDue(Now).Should().BeFalse();
        connection.BeginSync(Now).Errors[0].Code.Should().Be(BankConnectionErrors.NotActiveCode);
    }

    [Fact]
    public void Les_textes_trop_longs_sont_coupes()
    {
        BankConnection connection = BankConnection.Start(
            new string('p', 80), new string('i', 80), new string('n', 200), new string('r', 200), null, User, Now, Guid.CreateVersion7());

        connection.Provider.Should().HaveLength(BankConnection.MaxProviderLength);
        connection.InstitutionId.Should().HaveLength(BankConnection.MaxInstitutionIdLength);
        connection.InstitutionName.Should().HaveLength(BankConnection.MaxInstitutionNameLength);
        connection.ProviderReference.Should().HaveLength(BankConnection.MaxReferenceLength);
    }

    [Fact]
    public void Le_consentement_donne_garde_le_compte_masque_et_son_echeance()
    {
        BankConnection connection = Active();

        connection.Status.Should().Be(BankConnectionStatus.Active);
        connection.AccountDisplay.Should().Be("FR76 •••• 0143");
        connection.ConsentExpiresAt.Should().Be(Now.AddDays(180));
        connection.ActivatedAt.Should().Be(Now);
        connection.IsSyncDue(Now).Should().BeTrue("jamais relu");
        connection.Activate("FR7630004000031234567890143", Now.AddDays(180), Now).Errors[0].Code.Should().Be(BankConnectionErrors.AlreadyCompletedCode);
    }

    [Fact]
    public void Un_consentement_deja_echu_est_refuse_et_un_numero_de_compte_est_masque()
    {
        Pending().Activate("FR76", Now, Now).Errors[0].Code.Should().Be(BankConnectionErrors.ConsentExpiredCode);

        BankConnection account = Pending();
        account.Activate("12345678901", Now.AddDays(1), Now).IsSuccess.Should().BeTrue();
        account.AccountDisplay.Should().Be("•••• 8901");
    }

    [Fact]
    public void L_accord_est_a_renouveler_dans_ses_quinze_derniers_jours_puis_echu()
    {
        BankConnection connection = Active();

        connection.NeedsRenewal(Now.AddDays(164)).Should().BeFalse();
        connection.NeedsRenewal(Now.AddDays(165)).Should().BeTrue();
        connection.IsExpired(Now.AddDays(179)).Should().BeFalse();
        connection.IsExpired(Now.AddDays(180)).Should().BeTrue();
        connection.IsActive(Now.AddDays(180)).Should().BeFalse();
        connection.BeginSync(Now.AddDays(180)).Errors[0].Code.Should().Be(BankConnectionErrors.ConsentExpiredCode);
    }

    [Fact]
    public void Une_seule_synchronisation_a_la_fois_et_une_reservation_abandonnee_expire()
    {
        BankConnection connection = Active();

        connection.BeginSync(Now).IsSuccess.Should().BeTrue();
        connection.BeginSync(Now.AddMinutes(1)).Errors[0].Code.Should().Be(BankConnectionErrors.SyncInProgressCode);
        connection.BeginSync(Now.Add(BankConnection.SyncLease)).IsSuccess.Should().BeTrue("le processus qui l'avait réservée s'est arrêté");

        connection.SyncFailed();
        connection.SyncStartedAt.Should().BeNull();
        connection.LastSyncAt.Should().BeNull();
    }

    [Fact]
    public void Un_compte_relu_se_relit_six_heures_plus_tard()
    {
        BankConnection connection = Active();
        long version = connection.Version;
        connection.BeginSync(Now).IsSuccess.Should().BeTrue();

        connection.Synced(4, Now);

        connection.Should().BeEquivalentTo(new { LastSyncAt = (DateTimeOffset?)Now, LastSyncImported = 4, SyncStartedAt = (DateTimeOffset?)null });
        connection.Version.Should().Be(version + 2);
        connection.IsSyncDue(Now.AddHours(5)).Should().BeFalse();
        connection.IsSyncDue(Now.Add(BankConnection.SyncInterval)).Should().BeTrue();
    }

    [Fact]
    public void Deconnecter_est_definitif_et_libere_la_reservation()
    {
        BankConnection connection = Active();
        connection.BeginSync(Now).IsSuccess.Should().BeTrue();

        connection.Revoke(User, Now).IsSuccess.Should().BeTrue();

        connection.Should().BeEquivalentTo(new { Status = BankConnectionStatus.Revoked, RevokedAt = (DateTimeOffset?)Now, RevokedBy = (Guid?)User, SyncStartedAt = (DateTimeOffset?)null });
        connection.NeedsRenewal(Now.AddDays(179)).Should().BeFalse();
        connection.Revoke(User, Now).Errors[0].Code.Should().Be(BankConnectionErrors.NotActiveCode);
        connection.BeginSync(Now).Errors[0].Code.Should().Be(BankConnectionErrors.NotActiveCode);
    }

    [Fact]
    public void Un_renouvellement_garde_la_connexion_qu_il_remplace()
    {
        Guid old = Guid.CreateVersion7();

        Pending(old).ReplacesId.Should().Be(old);
    }
}
