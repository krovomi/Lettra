using Lettra.Domain.Common;
using Lettra.Domain.Preferences;

namespace Lettra.Domain.Tests.Preferences;

public sealed class UserPreferencesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Dossier = Guid.CreateVersion7();

    private static UserPreferences Fresh() => UserPreferences.Empty(Guid.CreateVersion7());

    [Fact]
    public void Un_compte_neuf_n_a_rien_de_regle()
    {
        UserPreferences preferences = Fresh();

        preferences.Version.Should().Be(0);
        preferences.Workspace.Should().Be(WorkspacePreferences.Empty);
        preferences.Workspace.RankByUsage.Should().BeTrue("les suggestions par usage sont actives par défaut");
        preferences.EntryDefaults.Should().BeEmpty();
        preferences.Usage.Should().BeEmpty();
        preferences.DefaultsFor(Dossier).Should().Be(new EntryDefaults { DossierId = Dossier });
    }

    [Fact]
    public void Un_reglage_valide_est_normalise()
    {
        UserPreferences preferences = Fresh();
        Guid favori = Guid.CreateVersion7();
        WorkspacePreferences workspace = new()
        {
            Theme = "Dark",
            Density = "Compact",
            Language = "en-US",
            HomePage = "billing/invoices",
            NavFavorites = ["billing/invoices", "billing/quotes", "billing/invoices"],
            CollapsedSections = ["Nav.Group.Settings", "Nav.Group.Billing"],
            FavoriteDossiers = [favori, favori],
            RankByUsage = false,
        };
        EntryDefaults defaults = new() { DossierId = Dossier, VatRateCode = " FR-20 ", PaymentTerms = "  ", QuoteValidityDays = 45 };

        preferences.Change(workspace, [defaults, new EntryDefaults { DossierId = Guid.CreateVersion7(), Unit = " " }], Now).IsSuccess.Should().BeTrue();

        preferences.Workspace.NavFavorites.Should().Equal("billing/invoices", "billing/quotes");
        preferences.Workspace.CollapsedSections.Should().Equal("Nav.Group.Billing", "Nav.Group.Settings");
        preferences.Workspace.FavoriteDossiers.Should().Equal(favori);
        preferences.Workspace.RankByUsage.Should().BeFalse();
        preferences.EntryDefaults.Should().ContainSingle("un dossier sans aucune valeur n'est pas retenu");
        preferences.DefaultsFor(Dossier).Should().Be(new EntryDefaults { DossierId = Dossier, VatRateCode = "FR-20", QuoteValidityDays = 45 });
        preferences.UpdatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("Theme", "<script>")]
    [InlineData("Language", "francais")]
    [InlineData("HomePage", "https://exemple.fr")]
    [InlineData("HomePage", "billing//invoices")]
    [InlineData("HomePage", "/billing")]
    public void Un_reglage_d_espace_invalide_est_refuse(string field, string value)
    {
        WorkspacePreferences workspace = field switch
        {
            "Theme" => new WorkspacePreferences { Theme = value },
            "Language" => new WorkspacePreferences { Language = value },
            _ => new WorkspacePreferences { HomePage = value },
        };
        UserPreferences preferences = Fresh();

        Result changed = preferences.Change(workspace, [], Now);

        changed.Errors.Should().ContainSingle().Which.Should().Be(UserPreferencesErrors.Invalid(field));
        preferences.Workspace.Should().Be(WorkspacePreferences.Empty, "rien n'est changé quand une valeur est refusée");
    }

    [Fact]
    public void Les_listes_sont_bornees()
    {
        WorkspacePreferences workspace = new()
        {
            NavFavorites = [.. Enumerable.Range(0, UserPreferences.MaxNavEntries + 1).Select(i => $"ecran-{i}")],
            CollapsedSections = ["1abc"],
            RecentDossiers = [.. Enumerable.Range(0, UserPreferences.MaxRecentDossiers + 1).Select(_ => Guid.CreateVersion7())],
            FavoriteDossiers = [Guid.Empty],
        };

        Result changed = Fresh().Change(workspace, [], Now);

        changed.Errors.Should().BeEquivalentTo(
        [
            UserPreferencesErrors.TooMany(nameof(WorkspacePreferences.NavFavorites)),
            UserPreferencesErrors.Invalid(nameof(WorkspacePreferences.CollapsedSections)),
            UserPreferencesErrors.Invalid(nameof(WorkspacePreferences.FavoriteDossiers)),
            UserPreferencesErrors.TooMany(nameof(WorkspacePreferences.RecentDossiers)),
        ]);
    }

    [Fact]
    public void Les_valeurs_de_saisie_invalides_sont_refusees_avec_leur_position()
    {
        EntryDefaults[] defaults =
        [
            new() { DossierId = Dossier, VatRateCode = "FR\n20", QuoteValidityDays = 0 },
            new() { DossierId = Guid.Empty, InvoiceNotes = new string('x', UserPreferences.MaxNotesLength + 1) },
        ];

        Result changed = Fresh().Change(WorkspacePreferences.Empty, defaults, Now);

        changed.Errors.Select(e => e.Target).Should().BeEquivalentTo(
            "EntryDefaults[0].VatRateCode", "EntryDefaults[0].QuoteValidityDays", "EntryDefaults[1].DossierId", "EntryDefaults[1].InvoiceNotes");
    }

    [Fact]
    public void Un_dossier_ne_peut_avoir_qu_un_jeu_de_valeurs_et_leur_nombre_est_borne()
    {
        Fresh().Change(WorkspacePreferences.Empty, [new() { DossierId = Dossier, Unit = "C62" }, new() { DossierId = Dossier, Unit = "HUR" }], Now)
            .Errors.Should().ContainSingle().Which.Should().Be(UserPreferencesErrors.Invalid(nameof(UserPreferences.EntryDefaults)));

        EntryDefaults[] many = [.. Enumerable.Range(0, UserPreferences.MaxDossiers + 1).Select(_ => new EntryDefaults { DossierId = Guid.CreateVersion7(), Unit = "C62" })];
        Fresh().Change(WorkspacePreferences.Empty, many, Now)
            .Errors.Should().ContainSingle().Which.Should().Be(UserPreferencesErrors.TooMany(nameof(UserPreferences.EntryDefaults)));
    }

    [Fact]
    public void Les_choix_les_plus_utilises_remontent_puis_les_plus_recents()
    {
        UserPreferences preferences = Fresh();
        Guid souvent = Guid.CreateVersion7();
        Guid recent = Guid.CreateVersion7();
        Guid ancien = Guid.CreateVersion7();

        preferences.RecordUse(Dossier, UsageKind.Customer, ancien, Now);
        preferences.RecordUse(Dossier, UsageKind.Customer, souvent, Now.AddMinutes(1));
        preferences.RecordUse(Dossier, UsageKind.Customer, souvent, Now.AddMinutes(2));
        preferences.RecordUse(Dossier, UsageKind.Customer, recent, Now.AddMinutes(3)).IsSuccess.Should().BeTrue();
        preferences.RecordUse(Dossier, UsageKind.CatalogItem, ancien, Now.AddMinutes(4));

        preferences.Ranked(Dossier, UsageKind.Customer).Select(e => (e.Id, e.Uses)).Should().Equal((souvent, 2), (recent, 1), (ancien, 1));
        preferences.Ranked(Dossier, UsageKind.CatalogItem).Should().ContainSingle();
        preferences.Ranked(Guid.CreateVersion7(), UsageKind.Customer).Should().BeEmpty("chaque dossier a ses propres choix");
        preferences.UpdatedAt.Should().Be(Now.AddMinutes(4));
    }

    [Fact]
    public void Les_choix_et_les_pieces_comptes_sont_bornes()
    {
        UserPreferences preferences = Fresh();
        Guid habituel = Guid.CreateVersion7();
        preferences.RecordUse(Dossier, UsageKind.Customer, habituel, Now);
        preferences.RecordUse(Dossier, UsageKind.Customer, habituel, Now);
        for (int i = 0; i < UserPreferences.MaxChoicesPerKind + 5; i++)
        {
            preferences.RecordUse(Dossier, UsageKind.Customer, Guid.CreateVersion7(), Now.AddMinutes(i));
        }

        List<Guid> factures = [];
        for (int i = 0; i < UserPreferences.MaxDocumentsPerKind + 3; i++)
        {
            Guid facture = Guid.CreateVersion7();
            factures.Add(facture);
            preferences.RecordUse(Dossier, UsageKind.Invoice, facture, Now.AddMinutes(i));
        }

        preferences.Ranked(Dossier, UsageKind.Customer).Should().HaveCount(UserPreferences.MaxChoicesPerKind)
            .And.Subject.First().Id.Should().Be(habituel, "le client le plus utilisé reste même quand d'autres arrivent");
        preferences.Ranked(Dossier, UsageKind.Invoice).Select(e => e.Id).Should().BeEquivalentTo(factures.TakeLast(UserPreferences.MaxDocumentsPerKind));
    }

    [Fact]
    public void Le_dernier_dossier_utilise_passe_en_tete_et_leur_nombre_est_borne()
    {
        UserPreferences preferences = Fresh();
        for (int i = 0; i < UserPreferences.MaxDossiers + 2; i++)
        {
            preferences.RecordUse(Guid.CreateVersion7(), UsageKind.Quote, Guid.CreateVersion7(), Now.AddMinutes(i));
        }

        preferences.RecordUse(Dossier, UsageKind.Quote, Guid.CreateVersion7(), Now.AddHours(2));

        preferences.Usage.Should().HaveCount(UserPreferences.MaxDossiers);
        preferences.Usage[0].DossierId.Should().Be(Dossier);
        preferences.Usage[0].LastUsed.Should().Be(Now.AddHours(2));
    }

    [Theory]
    [InlineData(true, false, UsageKind.Customer)]
    [InlineData(false, true, UsageKind.Customer)]
    [InlineData(false, false, (UsageKind)99)]
    public void Un_choix_sans_dossier_sans_identifiant_ou_d_un_genre_inconnu_est_refuse(bool emptyDossier, bool emptyId, UsageKind kind)
    {
        UserPreferences preferences = Fresh();

        preferences.RecordUse(emptyDossier ? Guid.Empty : Dossier, kind, emptyId ? Guid.Empty : Guid.CreateVersion7(), Now)
            .Errors.Should().ContainSingle().Which.Code.Should().Be(UserPreferencesErrors.InvalidCode);
        preferences.Usage.Should().BeEmpty();
    }

    [Fact]
    public void Oublier_un_dossier_ou_tous_et_tout_effacer()
    {
        UserPreferences preferences = Fresh();
        Guid autre = Guid.CreateVersion7();
        preferences.Change(new WorkspacePreferences { Theme = "Dark" }, [new() { DossierId = Dossier, Unit = "HUR" }], Now);
        preferences.RecordUse(Dossier, UsageKind.Customer, Guid.CreateVersion7(), Now);
        preferences.RecordUse(autre, UsageKind.Customer, Guid.CreateVersion7(), Now);

        preferences.ForgetUsage(Dossier, Now.AddDays(1));
        preferences.Usage.Select(u => u.DossierId).Should().Equal(autre);
        preferences.Workspace.Theme.Should().Be("Dark", "oublier les choix ne touche pas aux réglages");

        preferences.ForgetUsage(null, Now.AddDays(2));
        preferences.Usage.Should().BeEmpty();

        preferences.Reset(Now.AddDays(3));
        preferences.Workspace.Should().Be(WorkspacePreferences.Empty);
        preferences.EntryDefaults.Should().BeEmpty();
        preferences.UpdatedAt.Should().Be(Now.AddDays(3));
    }

    [Fact]
    public void Une_relecture_ecarte_ce_qui_ne_respecte_plus_les_bornes()
    {
        Guid user = Guid.CreateVersion7();
        Guid client = Guid.CreateVersion7();
        UsageEntry[] entries =
        [
            new(UsageKind.Customer, client, 3, Now),
            new(UsageKind.Customer, client, 1, Now),
            new(UsageKind.Customer, Guid.Empty, 2, Now),
            new(UsageKind.Customer, Guid.CreateVersion7(), 0, Now),
            new((UsageKind)42, Guid.CreateVersion7(), 2, Now),
        ];

        UserPreferences restored = UserPreferences.Restore(
            user,
            new WorkspacePreferences { HomePage = "javascript:alert(1)" },
            [new() { DossierId = Dossier, Unit = "C62" }, new() { DossierId = Dossier, Unit = "HUR" }, new() { DossierId = Guid.CreateVersion7() }, null!],
            [new DossierUsage(Dossier, entries), new DossierUsage(Guid.Empty, entries), new DossierUsage(Guid.CreateVersion7(), []), null!],
            7,
            Now);

        restored.UserId.Should().Be(user);
        (restored.Version, restored.UpdatedAt).Should().Be((7L, Now));
        restored.Workspace.Should().Be(WorkspacePreferences.Empty, "un espace de travail invalide revient aux valeurs de départ");
        restored.EntryDefaults.Should().ContainSingle().Which.Unit.Should().Be("C62");
        restored.Usage.Should().ContainSingle().Which.Entries.Should().Equal(new UsageEntry(UsageKind.Customer, client, 3, Now));
    }

    [Fact]
    public void Une_relecture_sans_contenu_donne_un_compte_vide()
    {
        UserPreferences restored = UserPreferences.Restore(Guid.CreateVersion7(), null, null, null, 2, Now);

        restored.Workspace.Should().Be(WorkspacePreferences.Empty);
        restored.EntryDefaults.Should().BeEmpty();
        restored.Usage.Should().BeEmpty();
        new DossierUsage(Dossier, []).LastUsed.Should().Be(DateTimeOffset.MinValue);
    }
}
