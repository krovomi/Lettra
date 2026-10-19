using System.Globalization;
using Bunit;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Security;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Security;
using Lettra.UI.Shared.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Security;

/// <summary>Coffre simulé du poste (MAUI : SecureStorage).</summary>
internal sealed class FakeDeviceLockStore : IDeviceLockStore
{
    public string? Value { get; set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Value);

    public Task WriteAsync(string? value, CancellationToken cancellationToken)
    {
        Value = value;
        return Task.CompletedTask;
    }
}

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class DeviceLockTests : BunitContext
{
    private static readonly DateTimeOffset Midi = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDeviceLockStore _store = new();
    private readonly ManualClock _clock = new(Midi);

    public DeviceLockTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private DeviceLock NewLock() => new(_store, _clock);

    [Fact]
    public async Task Le_code_est_hache_et_le_poste_demarre_verrouille()
    {
        DeviceLock first = NewLock();
        await first.LoadAsync(CancellationToken.None);
        first.HasPin.Should().BeFalse();
        (await first.SetPinAsync(null, "4821", CancellationToken.None)).Should().Be(PinCheck.Accepted);
        first.IsLocked.Should().BeFalse();
        _store.Value.Should().StartWith("v1|").And.NotContain("4821");

        DeviceLock restarted = NewLock();
        await restarted.LoadAsync(CancellationToken.None);
        restarted.IsLocked.Should().BeTrue();
        (await restarted.UnlockAsync("0000", CancellationToken.None)).Should().Be(PinCheck.WrongPin);
        restarted.IsLocked.Should().BeTrue();
        (await restarted.UnlockAsync("4821", CancellationToken.None)).Should().Be(PinCheck.Accepted);
        restarted.IsLocked.Should().BeFalse();

        restarted.Lock();
        restarted.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Apres_cinq_echecs_l_attente_double_et_survit_au_redemarrage()
    {
        DeviceLock device = NewLock();
        await device.SetPinAsync(null, "4821", CancellationToken.None);
        device.Lock();
        for (int i = 0; i < DeviceLock.FreeAttempts - 1; i++)
        {
            (await device.UnlockAsync("1111", CancellationToken.None)).Should().Be(PinCheck.WrongPin);
        }

        (await device.UnlockAsync("1111", CancellationToken.None)).Should().Be(PinCheck.Blocked);
        device.BlockedUntil.Should().Be(Midi.AddSeconds(30));
        (await device.UnlockAsync("4821", CancellationToken.None)).Should().Be(PinCheck.Blocked, "même le bon code attend la fin du délai");

        DeviceLock restarted = NewLock();
        await restarted.LoadAsync(CancellationToken.None);
        restarted.BlockedUntil.Should().Be(Midi.AddSeconds(30));

        _clock.Now = Midi.AddSeconds(31);
        (await restarted.UnlockAsync("1111", CancellationToken.None)).Should().Be(PinCheck.Blocked);
        restarted.BlockedUntil.Should().Be(Midi.AddSeconds(31 + 60));

        _clock.Now = Midi.AddMinutes(5);
        (await restarted.UnlockAsync("4821", CancellationToken.None)).Should().Be(PinCheck.Accepted);
        restarted.Lock();
        (await restarted.UnlockAsync("1111", CancellationToken.None)).Should().Be(PinCheck.WrongPin, "le succès remet le compteur à zéro");

        DeviceLock.Wait(0).Should().Be(TimeSpan.FromSeconds(30));
        DeviceLock.Wait(6).Should().Be(TimeSpan.FromSeconds(1920));
        DeviceLock.Wait(40).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Changer_ou_retirer_le_code_demande_le_code_actuel()
    {
        DeviceLock device = NewLock();
        (await device.SetPinAsync(null, "12", CancellationToken.None)).Should().Be(PinCheck.Invalid);
        (await device.SetPinAsync(null, "12ab", CancellationToken.None)).Should().Be(PinCheck.Invalid);
        await device.SetPinAsync(null, "4821", CancellationToken.None);

        (await device.SetPinAsync("0000", "999999", CancellationToken.None)).Should().Be(PinCheck.WrongPin);
        (await device.SetPinAsync("4821", "999999", CancellationToken.None)).Should().Be(PinCheck.Accepted);
        (await device.RemovePinAsync("4821", CancellationToken.None)).Should().Be(PinCheck.WrongPin);
        (await device.RemovePinAsync("999999", CancellationToken.None)).Should().Be(PinCheck.Accepted);
        device.HasPin.Should().BeFalse();
        _store.Value.Should().BeNull();
    }

    [Fact]
    public void Sans_coffre_pas_de_verrou()
    {
        Services.AddLettraUi();
        DeviceLock device = Services.GetRequiredService<DeviceLock>();
        device.IsAvailable.Should().BeFalse();

        Render<DeviceLockGate>(p => p.AddChildContent("<p id=\"app\">Lettra</p>")).Find("#app").Should().NotBeNull();
        Render<DevicePin>().Find("#pin-unavailable").TextContent.Should().Contain("navigateur");
    }

    [Fact]
    public async Task L_ecran_de_verrouillage_remplace_l_application_jusqu_au_bon_code()
    {
        await NewLock().SetPinAsync(null, "4821", CancellationToken.None);
        Services.AddSingleton<IDeviceLockStore>(_store);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddLettraUi();

        IRenderedComponent<DeviceLockGate> gate = Render<DeviceLockGate>(p => p.AddChildContent("<p id=\"app\">Lettra</p>"));
        gate.WaitForAssertion(() => gate.Find("#device-lock h1").TextContent.Should().Be("Lettra est verrouillé"));
        gate.FindAll("#app").Should().BeEmpty();

        gate.Find("#device-lock-pin").Change("0000");
        await gate.Find("form").SubmitAsync();
        gate.Find("#device-lock-message").TextContent.Should().Be("Code incorrect.");

        gate.Find("#device-lock-pin").Change("4821");
        await gate.Find("form").SubmitAsync();
        gate.WaitForAssertion(() => gate.Find("#app").TextContent.Should().Be("Lettra"));

        DeviceLock device = Services.GetRequiredService<DeviceLock>();
        await gate.InvokeAsync(device.Lock);
        gate.WaitForAssertion(() => gate.FindAll("#device-lock").Should().ContainSingle());
    }

    [Fact]
    public async Task L_ecran_du_code_le_definit_puis_le_change()
    {
        Services.AddSingleton<IDeviceLockStore>(_store);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddLettraUi();

        IRenderedComponent<DevicePin> page = Render<DevicePin>();
        page.WaitForAssertion(() => page.Find("#pin-state").TextContent.Should().StartWith("Aucun code PIN"));
        page.Find("#pin-new").Change("4821");
        page.Find("#pin-confirm").Change("4822");
        await page.Find("form").SubmitAsync();
        this.ToastText().Should().Be("Les deux saisies du nouveau code ne sont pas identiques.");

        page.Find("#pin-new").Change("4821");
        page.Find("#pin-confirm").Change("4821");
        await page.Find("form").SubmitAsync();
        this.ToastText().Should().Be("Code défini : l'application se verrouillera désormais.");
        page.Find("#pin-state").TextContent.Should().StartWith("Un code PIN protège ce poste");

        page.Find("#pin-current").Change("0000");
        page.Find("#pin-new").Change("123456");
        page.Find("#pin-confirm").Change("123456");
        await page.Find("form").SubmitAsync();
        this.ToastText().Should().Be("Code incorrect.");

        page.Find("#pin-lock-now").Click();
        Services.GetRequiredService<DeviceLock>().IsLocked.Should().BeTrue();
    }
}
