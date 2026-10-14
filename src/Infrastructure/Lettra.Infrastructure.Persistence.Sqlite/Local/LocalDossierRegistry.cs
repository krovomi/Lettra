using System.Text.Json;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// <summary>
/// Manifeste d'un dossier du poste (modes 1 et 4), posé à côté de son fichier .db : identité et DEK enveloppée.
/// Sans la clé du cabinet (coffre de l'OS), le manifeste ne permet pas d'ouvrir la base.
/// </summary>
public sealed record LocalDossierManifest(
    Guid DossierId,
    Guid OrganisationId,
    string Name,
    string DatabaseFile,
    byte[] WrappedDek,
    DateTimeOffset CreatedAt);

/// <summary>Le cabinet du poste : créé au premier lancement, il porte la clé maîtresse (KEK) dans le coffre de l'OS.</summary>
public sealed record LocalOrganisation(Guid Id, DateTimeOffset CreatedAt);

/// <summary>
/// Registre des dossiers du poste : un répertoire de données avec <c>organisation.json</c>, un manifeste
/// <c>{id}.lettra</c> et une base <c>{id}.db</c> par dossier, et <c>active.txt</c> pour le dossier ouvert.
/// </summary>
public sealed class LocalDossierRegistry : IDisposable
{
    public const string SecretPrefix = "local:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _active;

    public LocalDossierRegistry(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        DataDirectory = dataDirectory;
        string active = ActivePath;
        _active = File.Exists(active) && Guid.TryParse(File.ReadAllText(active), out Guid id) ? id : null;
    }

    public string DataDirectory { get; }

    /// <summary>Dossier ouvert sur le poste (lu une fois au démarrage, puis tenu à jour par <see cref="SetActiveAsync"/>).</summary>
    public Guid? ActiveDossierId => _active;

    public static string SecretRef(Guid dossierId) => $"{SecretPrefix}{dossierId:N}";

    public string DatabasePath(Guid dossierId) => Path.Combine(DataDirectory, $"{dossierId:N}.db");

    public async Task<LocalOrganisation> GetOrCreateOrganisationAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = Path.Combine(DataDirectory, "organisation.json");
            if (File.Exists(path))
            {
                return await ReadAsync<LocalOrganisation>(path, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("organisation.json illisible.");
            }

            LocalOrganisation organisation = new(Guid.CreateVersion7(), clock.GetUtcNow());
            await WriteAsync(path, organisation, cancellationToken).ConfigureAwait(false);
            return organisation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LocalDossierManifest?> FindAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        string path = ManifestPath(dossierId);
        return File.Exists(path) ? await ReadAsync<LocalDossierManifest>(path, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<IReadOnlyList<LocalDossierManifest>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(DataDirectory))
        {
            return [];
        }

        List<LocalDossierManifest> manifests = [];
        foreach (string file in Directory.EnumerateFiles(DataDirectory, "*.lettra"))
        {
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out Guid id)
                && await FindAsync(id, cancellationToken).ConfigureAwait(false) is { } manifest)
            {
                manifests.Add(manifest);
            }
        }

        return [.. manifests.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public Task SaveAsync(LocalDossierManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return WriteAsync(ManifestPath(manifest.DossierId), manifest, cancellationToken);
    }

    public async Task SetActiveAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(ActivePath, dossier.Value.ToString(), cancellationToken).ConfigureAwait(false);
        _active = dossier.Value;
    }

    /// <summary>Effacement par destruction de clé (ADR-0011) : la base reste sur le disque mais devient illisible.</summary>
    public async Task DestroyKeyAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        if (await FindAsync(dossierId, cancellationToken).ConfigureAwait(false) is { } manifest)
        {
            await SaveAsync(manifest with { WrappedDek = [] }, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _gate.Dispose();

    private string ActivePath => Path.Combine(DataDirectory, "active.txt");

    private string ManifestPath(Guid dossierId) => Path.Combine(DataDirectory, $"{dossierId:N}.lettra");

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        FileStream stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Écrit dans un fichier temporaire puis remplace : un arrêt brutal ne laisse pas de manifeste à moitié écrit.</summary>
    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DataDirectory);
        string temporary = path + ".tmp";
        FileStream stream = File.Create(temporary);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
