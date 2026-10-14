using Lettra.UI.Shared.Keyboard;

namespace Lettra.UI.Tests;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("F4", "F4")]
    [InlineData("ctrl+Delete", "Ctrl+Delete")]
    [InlineData("Ctrl+Shift+d", "Ctrl+Shift+D")]
    [InlineData("Shift+Alt+Ctrl+F10", "Ctrl+Alt+Shift+F10")]
    public void Parse_normalise_au_format_du_module_JS(string saisie, string attendu) =>
        KeyChord.Parse(saisie).ToString().Should().Be(attendu);

    [Fact]
    public void Parse_refuse_un_modificateur_inconnu()
    {
        Action parse = () => KeyChord.Parse("Hyper+F4");

        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_refuse_un_raccourci_vide()
    {
        Action parse = () => KeyChord.Parse(" + ");

        parse.Should().Throw<FormatException>();
    }
}

public sealed class ShortcutRegistryTests
{
    [Fact]
    public async Task Le_scope_le_plus_specifique_gagne_puis_le_global_reprend_apres_Dispose()
    {
        ShortcutRegistry registre = new();
        List<string> appels = [];
        registre.PushScope(new KeyboardScope("global").Bind("F4", "Global", _ => appels.Add("global")));
        IDisposable grille = registre.PushScope(new KeyboardScope("grille").Bind("F4", "Grille", _ => appels.Add("grille")));

        await registre.TryHandleAsync(KeyChord.Parse("F4"), CommandContext.None);
        grille.Dispose();
        await registre.TryHandleAsync(KeyChord.Parse("F4"), CommandContext.None);

        appels.Should().Equal("grille", "global");
    }

    [Fact]
    public async Task Une_commande_non_executable_laisse_la_main_au_scope_suivant()
    {
        ShortcutRegistry registre = new();
        List<string> appels = [];
        registre.PushScope(new KeyboardScope("global").Bind("F10", "Global", _ => appels.Add("global")));
        registre.PushScope(new KeyboardScope("grille").Bind("F10", "Valider", _ => appels.Add("grille"), ctx => ctx.Row >= 0));

        bool traite = await registre.TryHandleAsync(KeyChord.Parse("F10"), CommandContext.None);

        traite.Should().BeTrue();
        appels.Should().Equal("global");
    }

    [Fact]
    public async Task Une_touche_inconnue_n_est_pas_traitee()
    {
        ShortcutRegistry registre = new();
        registre.PushScope(new KeyboardScope("global").Bind("F1", "Aide", _ => { }));

        (await registre.TryHandleAsync(KeyChord.Parse("F7"), CommandContext.None)).Should().BeFalse();
    }

    [Fact]
    public void La_barre_affiche_chaque_touche_une_fois_triee_F1_a_F12()
    {
        ShortcutRegistry registre = new();
        registre.PushScope(new KeyboardScope("global").Bind("F10", "Global", _ => { }).Bind("F1", "Aide", _ => { })
            .Bind("Escape", "Fermer", _ => { }, showInBar: false));
        registre.PushScope(new KeyboardScope("grille").Bind("F10", "Valider", _ => { }).Bind("F4", "Liste", _ => { }).Bind("Insert", "Insérer", _ => { }));

        registre.VisibleBindings.Select(b => $"{b.Chord} {b.Label}")
            .Should().Equal("F1 Aide", "F4 Liste", "F10 Valider", "Insert Insérer");
    }

    [Fact]
    public void Le_registre_previent_la_barre_quand_un_scope_arrive_ou_part()
    {
        ShortcutRegistry registre = new();
        int changements = 0;
        registre.Changed += (_, _) => changements++;

        registre.PushScope(new KeyboardScope("grille")).Dispose();

        changements.Should().Be(2);
    }

    [Fact]
    public void KeysOf_donne_les_touches_a_confier_au_module_JS()
    {
        KeyboardScope scope = new KeyboardScope("grille").Bind("F4", "Liste", _ => { }).Bind("Ctrl+Delete", "Supprimer", _ => { });

        ShortcutRegistry.KeysOf(scope).Should().Equal("F4", "Ctrl+Delete");
    }

    [Fact]
    public void La_densite_donne_sa_classe_css_et_sa_hauteur_de_ligne()
    {
        UiDensity.Expert.CssClass().Should().Be("density-expert");
        UiDensity.Comfort.RowHeight().Should().Be(40f);
    }
}
