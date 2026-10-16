using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Dictée et commandes vocales du dossier ouvert (vague 5). Par HTTP, l'audio va au serveur Whisper de la cellule ;
/// sur le poste (MAUI), il est transcrit sur l'appareil, sans réseau. L'écran ne parle jamais à un moteur directement.
/// </summary>
public interface ISpeechGateway
{
    /// <summary>La voix sur ce déploiement : désactivée par défaut, l'écran n'affiche alors aucun micro.</summary>
    Task<OperationResult<SpeechStatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Transcrit un WAV PCM 16 bits mono 16 kHz (celui que produit l'enregistreur de l'interface).</summary>
    Task<OperationResult<TranscriptionResponse>> TranscribeAsync(ReadOnlyMemory<byte> wav, SpeechMode mode, string language, CancellationToken cancellationToken);
}
