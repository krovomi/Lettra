using Lettra.Calculations.Receipts;
using Lettra.UI.Shared.Services;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Receipts;

/// <summary>Ce qu'a donné la lecture d'une photo.</summary>
public enum ReceiptReadStatus
{
    /// <summary>Le texte a été lu ; les valeurs trouvées sont proposées (certaines peuvent manquer).</summary>
    Read,

    /// <summary>Ce navigateur ou cette WebView n'a pas WebAssembly SIMD : la lecture n'est pas possible ici.</summary>
    Unsupported,

    /// <summary>Le navigateur ne sait pas décoder l'image (HEIC hors Safari), ou c'est un PDF.</summary>
    Unreadable,

    /// <summary>Le moteur n'a pas pu se charger ou a échoué.</summary>
    Failed,
}

/// <summary>La lecture d'une pièce : son sort et ce qui est proposé.</summary>
public sealed record ReceiptReadOutcome(ReceiptReadStatus Status, ReceiptReading Reading);

/// <summary>
/// Lecture d'une photo de ticket ou de facture sur l'appareil : la reconnaissance du texte et son analyse se font ici,
/// rien n'est envoyé au serveur ni ailleurs. L'utilisateur confirme toujours ce qui est proposé.
/// </summary>
public interface IReceiptReader
{
    Task<ReceiptReadOutcome> ReadAsync(byte[] content, string contentType, CancellationToken cancellationToken);
}

/// <summary>Module JavaScript de la reconnaissance de texte (Tesseract en WebAssembly).</summary>
public static class ReceiptReaderModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-ocr.js";

    /// <summary>« ok », un saut de ligne et le texte lu ; ou le mot qui dit pourquoi rien n'a été lu.</summary>
    public static ReceiptReadOutcome Interpret(string? answer, DateOnly today) => answer switch
    {
        ['o', 'k', '\n', ..] => new ReceiptReadOutcome(ReceiptReadStatus.Read, ReceiptTextReader.Read(answer[3..], today)),
        "ok" => new ReceiptReadOutcome(ReceiptReadStatus.Read, ReceiptReading.Empty),
        "unsupported" => new ReceiptReadOutcome(ReceiptReadStatus.Unsupported, ReceiptReading.Empty),
        "unreadable" => new ReceiptReadOutcome(ReceiptReadStatus.Unreadable, ReceiptReading.Empty),
        _ => new ReceiptReadOutcome(ReceiptReadStatus.Failed, ReceiptReading.Empty),
    };
}

/// <summary>
/// Lecture par <c>lettra-ocr.js</c> (Tesseract, modèle français livré avec Lettra), puis <see cref="ReceiptTextReader"/>.
/// Le texte lu reste en mémoire le temps de l'analyse : il n'est ni gardé, ni journalisé, ni envoyé.
/// </summary>
internal sealed class OnDeviceReceiptReader(IJSRuntime js, TimeProvider clock) : IReceiptReader, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<ReceiptReadOutcome> ReadAsync(byte[] content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!contentType.StartsWith("image/", StringComparison.Ordinal))
        {
            return new ReceiptReadOutcome(ReceiptReadStatus.Unreadable, ReceiptReading.Empty);
        }

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ReceiptReaderModule.Path);
            string answer = await _module.InvokeAsync<string>("read", cancellationToken, content, contentType);
            return ReceiptReaderModule.Interpret(answer, clock.LocalToday());
        }
        catch (JSException)
        {
            return new ReceiptReadOutcome(ReceiptReadStatus.Failed, ReceiptReading.Empty);
        }
    }

    public async ValueTask DisposeAsync() => await JsModules.ReleaseAsync(_module, "release").ConfigureAwait(false);
}
