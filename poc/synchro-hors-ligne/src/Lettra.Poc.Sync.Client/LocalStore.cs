using System.Globalization;
using System.Text.Json;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Domain;
using Microsoft.Data.Sqlite;

namespace Lettra.Poc.Sync.Client;

public enum OutboxKind { Fact, Intent }
public enum OutboxStatus { Pending, Sent, Rejected, Conflict }

public sealed record OutboxEntry(
    long Sequence, OutboxKind Kind, Guid IdempotencyKey, Guid InvoiceId, string Payload,
    DateTimeOffset CreatedAt, OutboxStatus Status, string? Detail);

public sealed record LocalInvoice(InvoiceDto Invoice, int? ServerVersion, bool PendingValidation);

public sealed record StoredConflict(Guid InvoiceId, string Field, JsonElement Base, JsonElement Mine, JsonElement Server);

/// <summary>
/// Base locale du poste. En MAUI : SQLCipher, clé dans le coffre de l'OS. Le point prouvé ici :
/// chaque écriture métier et son entrée d'outbox partent dans la même transaction.
/// </summary>
public sealed class LocalStore : IDisposable
{
    private readonly SqliteConnection _db;

    public LocalStore(string path)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS invoices (
                id TEXT PRIMARY KEY, json TEXT NOT NULL, server_version INTEGER NULL, pending_validation INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS outbox (
                seq INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, idempotency_key TEXT NOT NULL UNIQUE,
                invoice_id TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT NOT NULL,
                status TEXT NOT NULL, detail TEXT NULL);
            CREATE TABLE IF NOT EXISTS series (code TEXT PRIMARY KEY, last_seq INTEGER NOT NULL, last_hash TEXT NULL);
            CREATE TABLE IF NOT EXISTS conflicts (
                invoice_id TEXT NOT NULL, field TEXT NOT NULL, base TEXT NOT NULL, mine TEXT NOT NULL, server TEXT NOT NULL,
                PRIMARY KEY (invoice_id, field));
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit (id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, origin TEXT NOT NULL, text TEXT NOT NULL);
            """);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Unité de travail : tout ou rien, comme un SaveChanges EF Core avec l'outbox.</summary>
    public T InTransaction<T>(Func<SqliteTransaction, T> work)
    {
        using var tx = _db.BeginTransaction();
        var result = work(tx);
        tx.Commit();
        return result;
    }

    public void InTransaction(Action<SqliteTransaction> work) => InTransaction(tx => { work(tx); return 0; });

    // ----------------------------------------------------------------- factures

    public LocalInvoice? FindInvoice(Guid id, SqliteTransaction? tx = null)
    {
        using var cmd = Command("SELECT json, server_version, pending_validation FROM invoices WHERE id = $id", tx, ("$id", id.ToString()));
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new LocalInvoice(Deserialize<InvoiceDto>(r.GetString(0)), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetInt32(2) == 1)
            : null;
    }

    public IReadOnlyList<LocalInvoice> AllInvoices()
    {
        using var cmd = Command("SELECT json, server_version, pending_validation FROM invoices", null);
        using var r = cmd.ExecuteReader();
        var list = new List<LocalInvoice>();
        while (r.Read())
            list.Add(new LocalInvoice(Deserialize<InvoiceDto>(r.GetString(0)), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetInt32(2) == 1));
        return list;
    }

    public void SaveInvoice(SqliteTransaction tx, InvoiceDto invoice, int? serverVersion, bool pendingValidation)
        => Command("""
            INSERT INTO invoices (id, json, server_version, pending_validation) VALUES ($id, $json, $v, $p)
            ON CONFLICT(id) DO UPDATE SET json = excluded.json, server_version = excluded.server_version,
                                          pending_validation = excluded.pending_validation
            """, tx, ("$id", invoice.Id.ToString()), ("$json", Serialize(invoice)), ("$v", serverVersion), ("$p", pendingValidation ? 1 : 0))
            .ExecuteAndDispose();

    // ----------------------------------------------------------------- outbox

    public void Enqueue(SqliteTransaction tx, OutboxKind kind, Guid idempotencyKey, Guid invoiceId, object payload, DateTimeOffset at)
        => Command("""
            INSERT INTO outbox (kind, idempotency_key, invoice_id, payload, created_at, status)
            VALUES ($k, $key, $inv, $payload, $at, 'Pending')
            """, tx, ("$k", kind.ToString()), ("$key", idempotencyKey.ToString()), ("$inv", invoiceId.ToString()),
            ("$payload", JsonSerializer.Serialize(payload, payload.GetType() == typeof(ValidatedInvoiceFact) ? typeof(ValidatedInvoiceFact) : typeof(DraftCommand), SyncHttp.Json)),
            ("$at", at.ToString("O", CultureInfo.InvariantCulture)))
            .ExecuteAndDispose();

    public IReadOnlyList<OutboxEntry> Outbox(OutboxStatus? status = null, OutboxKind? kind = null)
    {
        using var cmd = Command("""
            SELECT seq, kind, idempotency_key, invoice_id, payload, created_at, status, detail FROM outbox
            WHERE ($s IS NULL OR status = $s) AND ($k IS NULL OR kind = $k) ORDER BY seq
            """, null, ("$s", status?.ToString()), ("$k", kind?.ToString()));
        using var r = cmd.ExecuteReader();
        var list = new List<OutboxEntry>();
        while (r.Read())
            list.Add(new OutboxEntry(r.GetInt64(0), Enum.Parse<OutboxKind>(r.GetString(1)), Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)), r.GetString(4), DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                Enum.Parse<OutboxStatus>(r.GetString(6)), r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    public bool HasPendingIntents(Guid invoiceId, SqliteTransaction tx)
    {
        using var cmd = Command("SELECT COUNT(*) FROM outbox WHERE invoice_id = $id AND kind = 'Intent' AND status = 'Pending'", tx, ("$id", invoiceId.ToString()));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Une entrée n'est jamais réécrite après sa mise en file : seul son statut change.</summary>
    public void MarkOutbox(SqliteTransaction tx, Guid idempotencyKey, OutboxStatus status, string? detail)
        => Command("UPDATE outbox SET status = $s, detail = $d WHERE idempotency_key = $key", tx,
            ("$s", status.ToString()), ("$d", detail), ("$key", idempotencyKey.ToString())).ExecuteAndDispose();

    // ----------------------------------------------------------------- série du poste

    public (long LastSequence, string? LastHash) Series(SqliteTransaction tx, string code)
    {
        using var cmd = Command("SELECT last_seq, last_hash FROM series WHERE code = $c", tx, ("$c", code));
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1)) : (0, null);
    }

    public void SaveSeries(SqliteTransaction tx, string code, long lastSequence, string? lastHash)
        => Command("""
            INSERT INTO series (code, last_seq, last_hash) VALUES ($c, $s, $h)
            ON CONFLICT(code) DO UPDATE SET last_seq = excluded.last_seq, last_hash = excluded.last_hash
            """, tx, ("$c", code), ("$s", lastSequence), ("$h", lastHash)).ExecuteAndDispose();

    // ----------------------------------------------------------------- conflits

    public void SaveConflict(SqliteTransaction tx, Guid invoiceId, FieldConflict c)
        => Command("""
            INSERT INTO conflicts (invoice_id, field, base, mine, server) VALUES ($i, $f, $b, $m, $s)
            ON CONFLICT(invoice_id, field) DO UPDATE SET base = excluded.base, mine = excluded.mine, server = excluded.server
            """, tx, ("$i", invoiceId.ToString()), ("$f", c.Field), ("$b", c.Base.GetRawText()),
            ("$m", c.Mine.GetRawText()), ("$s", c.Server.GetRawText())).ExecuteAndDispose();

    public IReadOnlyList<StoredConflict> Conflicts(Guid? invoiceId = null, SqliteTransaction? tx = null)
    {
        using var cmd = Command("SELECT invoice_id, field, base, mine, server FROM conflicts WHERE $i IS NULL OR invoice_id = $i ORDER BY field",
            tx, ("$i", invoiceId?.ToString()));
        using var r = cmd.ExecuteReader();
        var list = new List<StoredConflict>();
        while (r.Read())
            list.Add(new StoredConflict(Guid.Parse(r.GetString(0)), r.GetString(1),
                JsonDocument.Parse(r.GetString(2)).RootElement, JsonDocument.Parse(r.GetString(3)).RootElement,
                JsonDocument.Parse(r.GetString(4)).RootElement));
        return list;
    }

    public void DeleteConflicts(SqliteTransaction tx, Guid invoiceId)
        => Command("DELETE FROM conflicts WHERE invoice_id = $i", tx, ("$i", invoiceId.ToString())).ExecuteAndDispose();

    // ----------------------------------------------------------------- méta, audit, effacement

    public string? GetMeta(string key, SqliteTransaction? tx = null)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $k", tx, ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(SqliteTransaction? tx, string key, string value)
        => Command("INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            tx, ("$k", key), ("$v", value)).ExecuteAndDispose();

    public void Audit(SqliteTransaction? tx, DateTimeOffset at, string origin, string text)
        => Command("INSERT INTO audit (at, origin, text) VALUES ($a, $o, $t)", tx,
            ("$a", at.ToString("O", CultureInfo.InvariantCulture)), ("$o", origin), ("$t", text)).ExecuteAndDispose();

    public IReadOnlyList<(string Origin, string Text)> AuditTrail()
    {
        using var cmd = Command("SELECT origin, text FROM audit ORDER BY id", null);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Appareil révoqué : effacement des données locales du dossier.</summary>
    public void Wipe()
        => Execute("DELETE FROM invoices; DELETE FROM outbox; DELETE FROM series; DELETE FROM conflicts; DELETE FROM meta; DELETE FROM audit;");

    // -----------------------------------------------------------------

    private void Execute(string sql) { using var cmd = _db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private SqliteCommand Command(string sql, SqliteTransaction? tx, params (string Name, object? Value)[] parameters)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SyncHttp.Json);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, SyncHttp.Json)!;
}

internal static class SqliteCommandExtensions
{
    public static void ExecuteAndDispose(this SqliteCommand cmd) { using (cmd) cmd.ExecuteNonQuery(); }
}
