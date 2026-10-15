using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lettra.UI.Gateway.Http.Device;

/// <summary>
/// Retour de la connexion par une adresse de bouclage (RFC 8252 §7.3) : le poste écoute sur <c>127.0.0.1</c>, sur un port
/// libre tiré au moment de la connexion, et ouvre la page du cabinet dans le navigateur système. Seule la requête
/// <c>GET /callback</c> est lue ; le navigateur reçoit une page qui l'invite à revenir dans Lettra.
/// </summary>
public sealed class LoopbackAuthorizationBrowser(Func<Uri, Task> openBrowser) : IAuthorizationBrowser, IDisposable
{
    /// <summary>Délai laissé à l'utilisateur pour se connecter (second facteur compris).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private const int MaxRequestLength = 16 * 1024;

    private TcpListener? _listener;

    public Uri PrepareRedirectUri()
    {
        _listener?.Stop();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}/callback");
    }

    public async Task<Uri?> AuthorizeAsync(Uri authorizeUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        TcpListener listener = _listener ?? throw new InvalidOperationException("PrepareRedirectUri doit être appelé avant AuthorizeAsync.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            // L'écoute commence avant l'ouverture du navigateur : le retour peut arriver très vite (session SSO déjà ouverte).
            Task<Uri?> callback = WaitForCallbackAsync(listener, redirectUri, timeout.Token);
            await openBrowser(authorizeUrl).ConfigureAwait(false);
            return await callback.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            listener.Stop();
            _listener = null;
        }
    }

    private static async Task<Uri?> WaitForCallbackAsync(TcpListener listener, Uri redirectUri, CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();
            string? target = await ReadRequestTargetAsync(stream, cancellationToken).ConfigureAwait(false);
            if (target is not null && target.StartsWith(redirectUri.AbsolutePath, StringComparison.Ordinal))
            {
                await RespondAsync(stream, HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);
                return new Uri(redirectUri.GetLeftPart(UriPartial.Authority) + target);
            }

            // Favicon et autres requêtes du navigateur : ignorées.
            await RespondAsync(stream, HttpStatusCode.NotFound, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _listener?.Stop();

    /// <summary>Lit la ligne de requête (<c>GET /callback?code=… HTTP/1.1</c>) et renvoie sa cible.</summary>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxRequestLength];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        string firstLine = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n")[0];
        string[] parts = firstLine.Split(' ');
        return parts.Length == 3 && parts[0] == "GET" && parts[1].StartsWith('/') ? parts[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, HttpStatusCode status, CancellationToken cancellationToken)
    {
        string body = status == HttpStatusCode.OK
            ? "<!doctype html><html lang=\"fr\"><meta charset=\"utf-8\"><title>Lettra</title><p>Connexion terminée. Vous pouvez revenir dans Lettra et fermer cet onglet.</p><p lang=\"en\">Signed in. You can go back to Lettra and close this tab.</p></html>"
            : string.Empty;
        byte[] content = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
