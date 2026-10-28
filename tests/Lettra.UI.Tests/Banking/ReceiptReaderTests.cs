using Bunit;
using Lettra.Calculations.Receipts;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Receipts;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Lettra.UI.Tests.Banking;

/// <summary>La lecture des pièces sur l'appareil : le module JavaScript lit, Lettra interprète, rien d'autre ne sort.</summary>
public sealed class ReceiptReaderTests : BunitContext, IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 28);
    private readonly BunitJSModuleInterop _ocr;

    public ReceiptReaderTests()
    {
        _ocr = JSInterop.SetupModule(ReceiptReaderModule.Path);
        _ocr.SetupVoid("release").SetVoidResult();
        Services.AddLettraUi();
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // Le lecteur ne se libère qu'en asynchrone, comme dans le navigateur.
    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    private IReceiptReader Reader => Services.GetRequiredService<IReceiptReader>();

    [Fact]
    public void La_reponse_du_module_devient_une_proposition()
    {
        ReceiptReaderModule.Interpret("ok\nTOTAL TTC 12,00\nTVA 20% 2,00\n03/10/2026", Today)
            .Should().Be(new ReceiptReadOutcome(ReceiptReadStatus.Read, new ReceiptReading(12m, new DateOnly(2026, 10, 3), 2m, 20m)));
        ReceiptReaderModule.Interpret("ok", Today).Should().Be(new ReceiptReadOutcome(ReceiptReadStatus.Read, ReceiptReading.Empty));
        ReceiptReaderModule.Interpret("unsupported", Today).Status.Should().Be(ReceiptReadStatus.Unsupported);
        ReceiptReaderModule.Interpret("unreadable", Today).Status.Should().Be(ReceiptReadStatus.Unreadable);
        ReceiptReaderModule.Interpret("error", Today).Status.Should().Be(ReceiptReadStatus.Failed);
        ReceiptReaderModule.Interpret(null, Today).Status.Should().Be(ReceiptReadStatus.Failed);
    }

    [Fact]
    public async Task Une_photo_est_lue_par_le_module_un_pdf_ne_l_est_pas()
    {
        _ocr.Setup<string>("read", _ => true).SetResult("ok\nNET A PAYER 84,90\nDONT TVA 20,00% 14,15\n15 octobre 2026");

        ReceiptReadOutcome photo = await Reader.ReadAsync([0xFF, 0xD8, 0xFF], "image/jpeg", CancellationToken.None);
        ReceiptReadOutcome pdf = await Reader.ReadAsync([0x25, 0x50], "application/pdf", CancellationToken.None);

        photo.Should().Be(new ReceiptReadOutcome(ReceiptReadStatus.Read, new ReceiptReading(84.90m, new DateOnly(2026, 10, 15), 14.15m, 20m)));
        pdf.Status.Should().Be(ReceiptReadStatus.Unreadable);
        _ocr.Invocations.Where(i => i.Identifier == "read").Should().ContainSingle();
        await ((IAsyncDisposable)Reader).DisposeAsync();
        _ocr.VerifyInvoke("release");
    }

    [Fact]
    public async Task Un_moteur_en_echec_rend_la_saisie_a_l_utilisateur()
    {
        _ocr.Setup<string>("read", _ => true).SetException(new JSException("wasm"));

        (await Reader.ReadAsync([0xFF], "image/png", CancellationToken.None)).Status.Should().Be(ReceiptReadStatus.Failed);
    }
}
