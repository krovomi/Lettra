using System.Collections;
using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lettra.Infrastructure.Persistence.Security;

/// <summary>
/// Chiffre et déchiffre les champs sensibles d'un contexte de dossier (<see cref="SensitiveField"/>), au plus près du
/// serveur : chaque paramètre marqué d'une commande est chiffré avant l'envoi, et toute valeur chiffrée lue est
/// déchiffrée, quelle que soit la requête (entité, projection, ExecuteUpdate). Un intercepteur par contexte : il porte les
/// clés du dossier ouvert.
/// </summary>
internal sealed class SensitiveFieldInterceptor(SensitiveFieldCipher? cipher) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Protect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Protect(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Protect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Protect(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Protect(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Protect(command);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) =>
        Reveal(eventData, result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Reveal(eventData, result));

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) =>
        DecryptingDataReader.Reveal(result, cipher);

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(DecryptingDataReader.Reveal(result, cipher));

    /// <summary>
    /// Résultats d'une requête : lus à travers le déchiffrement. Ceux d'un enregistrement (valeurs générées par le serveur)
    /// ou d'une migration ne portent aucun champ sensible et restent ceux du fournisseur, qui en a besoin tels quels.
    /// </summary>
    private DbDataReader Reveal(CommandExecutedEventData eventData, DbDataReader result) =>
        eventData.CommandSource is CommandSource.SaveChanges or CommandSource.Migrations ? result : new DecryptingDataReader(result, cipher);

    /// <summary>Paramètres marqués : chiffrés avec la clé du dossier, ou simplement démarqués s'il n'y en a pas (poste).</summary>
    private void Protect(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is string value && value.StartsWith(SensitiveField.PendingMarker, StringComparison.Ordinal))
            {
                string clear = SensitiveField.Unmark(value);
                parameter.Value = cipher is null ? clear : cipher.Encrypt(clear);
            }
        }
    }
}

/// <summary>Lecteur qui rend en clair les valeurs chiffrées ; tout le reste passe tel quel au lecteur du provider.</summary>
internal sealed class DecryptingDataReader(DbDataReader inner, SensitiveFieldCipher? cipher) : DbDataReader
{
    public override int Depth => inner.Depth;

    public override int FieldCount => inner.FieldCount;

    public override bool HasRows => inner.HasRows;

    public override bool IsClosed => inner.IsClosed;

    public override int RecordsAffected => inner.RecordsAffected;

    public override int VisibleFieldCount => inner.VisibleFieldCount;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>Une valeur chiffrée sans clé de dossier : le dossier est illisible (clé détruite ou absente).</summary>
    internal static object? Reveal(object? value, SensitiveFieldCipher? cipher) =>
        value is string text ? Reveal(text, cipher) : value;

    internal static string Reveal(string value, SensitiveFieldCipher? cipher) =>
        !value.StartsWith(SensitiveField.EncryptedPrefix, StringComparison.Ordinal)
            ? value
            : cipher?.Decrypt(value) ?? throw new CryptographicException("Clé du dossier indisponible : champ chiffré illisible.");

    public override string GetString(int ordinal) => Reveal(inner.GetString(ordinal), cipher);

    public override object GetValue(int ordinal) => Reveal(inner.GetValue(ordinal), cipher)!;

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int count = inner.GetValues(values);
        for (int i = 0; i < count; i++)
        {
            values[i] = Reveal(values[i], cipher)!;
        }

        return count;
    }

    public override T GetFieldValue<T>(int ordinal) =>
        typeof(T) == typeof(string) ? (T)(object)GetString(ordinal) : inner.GetFieldValue<T>(ordinal);

    public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
        typeof(T) == typeof(string)
            ? (T)(object)Reveal(await inner.GetFieldValueAsync<string>(ordinal, cancellationToken).ConfigureAwait(false), cipher)
            : await inner.GetFieldValueAsync<T>(ordinal, cancellationToken).ConfigureAwait(false);

    public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);

    public override byte GetByte(int ordinal) => inner.GetByte(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => inner.GetChar(ordinal);

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);

    public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);

    public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);

    public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);

    public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);

    public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);

    public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);

    public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);

    public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);

    public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);

    public override string GetName(int ordinal) => inner.GetName(ordinal);

    public override int GetOrdinal(string name) => inner.GetOrdinal(name);

    public override Type GetProviderSpecificFieldType(int ordinal) => inner.GetProviderSpecificFieldType(ordinal);

    public override object GetProviderSpecificValue(int ordinal) => inner.GetProviderSpecificValue(ordinal);

    public override int GetProviderSpecificValues(object[] values) => inner.GetProviderSpecificValues(values);

    public override System.Data.DataTable? GetSchemaTable() => inner.GetSchemaTable();

    public override Task<System.Data.DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default) =>
        inner.GetSchemaTableAsync(cancellationToken);

    public override Stream GetStream(int ordinal) => inner.GetStream(ordinal);

    public override TextReader GetTextReader(int ordinal) => inner.GetTextReader(ordinal);

    public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);

    public override bool NextResult() => inner.NextResult();

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);

    public override bool Read() => inner.Read();

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override void Close() => inner.Close();

    public override Task CloseAsync() => inner.CloseAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
