using Lettra.UI.Shared.Demo;
using Lettra.UI.Tests.Security;
using static Lettra.UI.Shared.Demo.DemoAction;

namespace Lettra.UI.Tests.Demo;

/// <summary>Lecteur de la démo commentée : ordre des gestes, pause, « Suivant », étape bloquée, arrêt, vitesse.</summary>
public sealed class DemoPlayerTests
{
    private static readonly DateTimeOffset Midi = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDemoDriver _driver = new();
    private readonly DemoPlayer _player;
    private int _delays;

    public DemoPlayerTests()
    {
        _player = new DemoPlayer(_driver, new ManualClock(Midi))
        {
            Delay = (_, _) =>
            {
                _delays++;
                return Task.CompletedTask;
            },
        };
        _player.Use(
        [
            new("one", "circle-play",
            [
                new("a", "#list", Go("customers"), Wait("#list")),
                new("b", "#name", Click("#new"), Type("#name", "Client {run}"), Type("#siren", "{siren}")),
            ]),
            new("two", "receipt",
            [
                new("c", null),
                new("d", "#subject", Type("#subject", "res:Demo.Value.Subject")),
            ]),
        ]);
    }

    [Fact]
    public async Task La_demo_joue_chaque_geste_dans_l_ordre_puis_se_termine()
    {
        _player.Resolve = value => value == "res:Demo.Value.Subject" ? "Formation" : value;

        await _player.StartAsync();

        string run = _player.Values.Run;
        string siren = _player.Values.Siren;
        _driver.Gestures.Should().Equal(
            "go customers", "wait #list", "show #list",
            "click #new", $"type #name=Client {run}", $"type #siren={siren}", "show #name",
            "show ",
            "type #subject=Formation", "show #subject");
        _player.State.Should().Be(DemoState.Finished);
        _driver.Cleared.Should().BeTrue();
        _player.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task La_demo_peut_commencer_a_un_chapitre()
    {
        await _player.StartAsync(1);

        _driver.Gestures.Should().NotContain(g => g.StartsWith("go ", StringComparison.Ordinal));
        _driver.Gestures.Should().Contain("type #subject=res:Demo.Value.Subject");
        Action horsScenario = () => _ = _player.StartAsync(2);
        horsScenario.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task La_pause_fige_l_etape_jusqu_a_la_reprise()
    {
        _driver.OnHighlight = target =>
        {
            if (target == "#name")
            {
                _player.Pause();
            }
        };

        Task run = _player.StartAsync();

        _player.State.Should().Be(DemoState.Paused);
        _player.Step!.Id.Should().Be("b");
        run.IsCompleted.Should().BeFalse();
        _player.Pause();   // déjà en pause : rien ne change
        _player.Resume();
        await run;
        _player.State.Should().Be(DemoState.Finished);
    }

    [Fact]
    public async Task Suivant_n_attend_pas_la_fin_de_la_lecture()
    {
        await _player.StartAsync();
        int sansSuivant = _delays;

        _delays = 0;
        _player.Delay = (_, _) =>
        {
            _delays++;
            _player.Next();
            return Task.CompletedTask;
        };
        await _player.StartAsync();

        _delays.Should().BeLessThan(sansSuivant);
        _player.State.Should().Be(DemoState.Finished);
    }

    [Fact]
    public async Task Un_element_absent_bloque_l_etape_et_reessayer_la_rejoue()
    {
        _driver.Missing.Add("#new");

        Task run = _player.StartAsync();

        _player.State.Should().Be(DemoState.Blocked);
        _player.Problem.Should().Be("#new");
        _player.Step!.Id.Should().Be("b");
        _player.Next();   // sans effet sur une étape bloquée
        _player.State.Should().Be(DemoState.Blocked);

        _driver.Missing.Clear();
        _player.Retry();
        await run;

        _driver.Gestures.Count(g => g == "click #new").Should().Be(2);
        _player.State.Should().Be(DemoState.Finished);
        _player.Problem.Should().BeNull();
    }

    [Fact]
    public async Task Passer_l_etape_laisse_ses_gestes_restants_de_cote()
    {
        _driver.Missing.Add("#new");

        Task run = _player.StartAsync();
        _player.Skip();
        await run;

        _driver.Gestures.Should().NotContain(g => g.StartsWith("type #name", StringComparison.Ordinal));
        _driver.Gestures.Should().Contain("type #subject=res:Demo.Value.Subject");
        _player.State.Should().Be(DemoState.Finished);
    }

    [Fact]
    public async Task L_element_a_montrer_absent_apres_les_gestes_bloque_aussi()
    {
        _driver.Missing.Add("#list");

        Task run = _player.StartAsync();

        _player.State.Should().Be(DemoState.Blocked);
        _player.Problem.Should().Be("#list");
        _player.Skip();
        await run;
        _player.State.Should().Be(DemoState.Finished);
    }

    [Fact]
    public async Task Arreter_rend_la_main_et_efface_l_anneau()
    {
        _driver.OnHighlight = _ => _player.Pause();
        Task run = _player.StartAsync();

        await _player.StopAsync();
        await run;

        _player.State.Should().Be(DemoState.Idle);
        _driver.Cleared.Should().BeTrue();
        await _player.StopAsync();   // déjà arrêtée
        _player.State.Should().Be(DemoState.Idle);
    }

    [Fact]
    public async Task Relancer_pendant_une_lecture_repart_du_debut()
    {
        _driver.OnHighlight = target =>
        {
            if (target == "#name")
            {
                _driver.OnHighlight = null;
                _player.Pause();
            }
        };
        Task first = _player.StartAsync();

        Task second = _player.StartAsync();
        await first;
        await second;

        _player.State.Should().Be(DemoState.Finished);
        _driver.Gestures.Count(g => g == "go customers").Should().Be(2);
    }

    [Fact]
    public void La_vitesse_change_et_ignore_les_valeurs_absurdes()
    {
        int changes = 0;
        _player.Changed += (_, _) => changes++;

        _player.SetSpeed(2);
        _player.SetSpeed(2);
        _player.SetSpeed(0);
        _player.SetSpeed(-1);

        _player.Speed.Should().Be(2);
        changes.Should().Be(1);
        DemoPlayer.Speeds.Should().Contain(1.0);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(100, 5.5)]
    [InlineData(10_000, 16)]
    public void Le_temps_de_lecture_suit_la_longueur_du_texte(int caracteres, double secondes) =>
        DemoPlayer.ReadingTime(caracteres).Should().Be(TimeSpan.FromSeconds(secondes));

    [Fact]
    public async Task Un_long_texte_reste_affiche_plus_longtemps()
    {
        await _player.StartAsync();
        int court = _delays;

        _delays = 0;
        _player.MeasureWith(_ => 150);
        await _player.StartAsync();

        _delays.Should().BeGreaterThan(court);
    }

    [Fact]
    public void Les_commandes_hors_lecture_ne_font_rien()
    {
        _player.Pause();
        _player.Resume();
        _player.Next();
        _player.Retry();
        _player.Skip();

        _player.State.Should().Be(DemoState.Idle);
        _player.Chapter!.Id.Should().Be("one");
        _player.Step!.Id.Should().Be("a");
        _player.Dispose();
    }
}
