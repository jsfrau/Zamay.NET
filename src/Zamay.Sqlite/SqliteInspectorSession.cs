using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Zamay.Core;

namespace Zamay.Sqlite;

/// <summary>Borrowed connection; never opens, closes, disposes or changes its transaction. Callers must keep it open
/// and avoid concurrent external use. Operations within this session are serialized. Counts are cached until refresh.
/// SQLite async commands may execute synchronously; cancellation and timeout are cooperative, not hard deadlines.</summary>
public sealed class SqliteInspectorSession
{
    private readonly SqliteConnection connection;
    private readonly SqliteTransaction? transaction;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, long> counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SqliteObject> known = new(StringComparer.Ordinal);
    public SqliteInspectionOptions Options { get; }
    public QueryDiagnostics? LastOperation { get; private set; }
    public SqliteInspectorSession(SqliteConnection connection, SqliteInspectionOptions? options = null, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.connection = connection; this.transaction = transaction;
        Options = options ?? new(); Options.Validate();
    }
    public long? GetCachedCount(string table) => counts.TryGetValue(table, out var n) ? n : null;
    private SqliteCommand Command(string sql)
    {
        if (connection.State != ConnectionState.Open) throw new InvalidOperationException("The borrowed SQLite connection must already be open.");
        var command = connection.CreateCommand(); command.CommandText = sql;
        command.CommandTimeout = Options.CommandTimeoutSeconds; command.Transaction = transaction;
        return command;
    }
    private SqliteObject Verified(string name) => known.TryGetValue(name, out var table) ? table : throw new ArgumentException("Load schema first; unknown object.", nameof(name));
    /// <summary>Reads main-schema metadata and clears cached counts. AutomaticCounts explicitly enables subsequent exact counts.</summary>
    public Task<SchemaSnapshot> LoadSchemaAsync(CancellationToken cancellationToken = default) => Task.Run(() => LoadSchemaCoreAsync(cancellationToken), cancellationToken);
    private async Task<SchemaSnapshot> LoadSchemaCoreAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        SchemaSnapshot snapshot;
        try
        {
            var watch = Stopwatch.StartNew();
            var headers = new List<(string Name, string Kind, string Sql)>();
            using (var command = Command("SELECT name,type,coalesce(sql,'') FROM main.sqlite_schema WHERE type IN ('table','view') AND (@system=1 OR substr(name,1,7) != 'sqlite_') ORDER BY type,name LIMIT @limit"))
            {
                command.Parameters.AddWithValue("@system", Options.IncludeSystemObjects ? 1 : 0);
                command.Parameters.AddWithValue("@limit", Options.MaxSchemaObjects + 1L);
                using var cancel = cancellationToken.Register(command.Cancel);
                using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                { cancellationToken.ThrowIfCancellationRequested(); headers.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2))); }
            }
            var objects = ImmutableArray.CreateBuilder<SqliteObject>();
            foreach (var h in headers.Take(Options.MaxSchemaObjects))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var columns = ImmutableArray.CreateBuilder<SqliteColumn>();
                using (var command = Command("SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo(@name,'main') ORDER BY cid LIMIT @limit"))
                {
                    command.Parameters.AddWithValue("@name", h.Name); command.Parameters.AddWithValue("@limit", 2001);
                    using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        columns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0, reader.GetInt32(3), reader.GetInt32(4)));
                }
                var indexes = ImmutableArray.CreateBuilder<SqliteIndex>();
                using (var command = Command("SELECT name,\"unique\",origin FROM pragma_index_list(@name,'main') LIMIT 201"))
                {
                    command.Parameters.AddWithValue("@name", h.Name);
                    using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) indexes.Add(new(reader.GetString(0), reader.GetInt32(1) != 0, reader.GetString(2)));
                }
                bool without = false;
                using (var command = Command("SELECT wr FROM pragma_table_list WHERE schema='main' AND name=@name LIMIT 1"))
                { command.Parameters.AddWithValue("@name", h.Name); without = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0; }
                var pk = columns.Where(c => c.PrimaryKeyOrdinal > 0).ToArray();
                string? order = null;
                if (h.Kind == "table" && !without)
                {
                    if (pk.Length == 1 && pk[0].DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) && !indexes.Any(i => i.Origin == "pk")) order = pk[0].Name;
                    else order = new[] { "rowid", "_rowid_", "oid" }.FirstOrDefault(n => !columns.Any(c => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
                }
                objects.Add(new(h.Name, h.Kind, DisplayText.Truncate(h.Sql, 16000), columns.Take(2000).ToImmutableArray(), indexes.Take(200).ToImmutableArray(), without, order,
                    columns.Count > 2000 || indexes.Count > 200 || h.Sql.Length > 16000));
            }
            known.Clear(); counts.Clear(); foreach (var obj in objects) known.Add(obj.Name, obj);
            snapshot = new(objects.ToImmutable(), headers.Count > Options.MaxSchemaObjects);
            LastOperation = new("Schema", watch.Elapsed, objects.Count, snapshot.Truncated, null, 0);
        }
        finally { gate.Release(); }
        if (Options.AutomaticCounts) foreach (var obj in snapshot.Objects.Where(o => o.Kind == "table")) await CountAsync(obj.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        return snapshot;
    }
    /// <summary>Exact COUNT(*) is explicit and may be expensive. Refresh invalidates the session cache.</summary>
    public Task<long> CountAsync(string name, bool refresh = false, CancellationToken cancellationToken = default) => Task.Run(() => CountCoreAsync(name, refresh, cancellationToken), cancellationToken);
    private async Task<long> CountCoreAsync(string name, bool refresh, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = Verified(name);
            if (!refresh && counts.TryGetValue(name, out var cached)) return cached;
            var watch = Stopwatch.StartNew();
            using var command = Command("SELECT COUNT(*) FROM main." + SqliteQueryBuilder.QuoteIdentifier(name));
            using var cancel = cancellationToken.Register(command.Cancel);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            cancellationToken.ThrowIfCancellationRequested();
            counts[name] = count; LastOperation = new("Exact count", watch.Elapsed, 1, false, null, 0);
            return count;
        }
        finally { gate.Release(); }
    }
    /// <summary>Reloads one selected row by its complete non-null, untruncated primary key. Returns all known columns
    /// in bounded column batches; the caller may supply a transaction for a consistent snapshot across batches.</summary>
    public Task<TableNode> ReadRecordAsync(PageResult page, int rowIndex, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(page.Owner, this)) throw new ArgumentException("Page belongs to a different session.", nameof(page));
        if (rowIndex < 0 || rowIndex >= page.RecordKeys.Length) throw new ArgumentOutOfRangeException(nameof(rowIndex));
        var keys = page.RecordKeys[rowIndex] ?? throw new InvalidOperationException("No complete non-null primary key is available. Only loaded record details can be shown.");
        return ReadRecordByPrimaryKeyAsync(page.SourceName, keys, cancellationToken);
    }
    /// <summary>Explicit record lookup. Values are parameterized; every primary-key component is required.</summary>
    public Task<TableNode> ReadRecordByPrimaryKeyAsync(string name, IReadOnlyDictionary<string, object?> keyValues, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        var snapshot = keyValues.ToImmutableDictionary();
        return Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var table = Verified(name);
                var pk = table.Columns.Where(c => c.PrimaryKeyOrdinal > 0).OrderBy(c => c.PrimaryKeyOrdinal).ToArray();
                if (pk.Length == 0 || pk.Length != snapshot.Count || pk.Any(c => !snapshot.TryGetValue(c.Name, out var v) || v is null or DBNull))
                    throw new ArgumentException("Every non-null primary-key component is required.", nameof(keyValues));
                var filters = pk.Select(c => new SqliteFilter(c.Name, FilterOperator.Equal, snapshot[c.Name])).ToImmutableArray();
                var columns = ImmutableArray.CreateBuilder<TableColumn>();
                var cells = ImmutableArray.CreateBuilder<DisplayNode>();
                foreach (var batch in table.Columns.Where(c => c.Hidden != 1).Chunk(Options.MaxColumns))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = await ReadPageLockedAsync(name, new() { Columns = batch.Select(c => c.Name).ToImmutableArray(), AdditionalFilters = filters }, Options with { PageSize = 1 }, cancellationToken).ConfigureAwait(false);
                    if (page.HasMore) throw new InvalidOperationException("Primary key no longer identifies a unique record; refresh schema.");
                    if (page.Table.Rows.Length == 0) return new(name, [], [], 0, Notice: new PlaceholderNode(PlaceholderReason.Descriptor, "Record no longer exists."));
                    columns.AddRange(page.Table.Columns); cells.AddRange(page.Table.Rows[0]);
                }
                return new TableNode(name, columns.ToImmutable(), [cells.ToImmutable()], 1, Notice: table.SchemaTruncated ? new PlaceholderNode(PlaceholderReason.MoreItemsMayExist, "Schema metadata truncated") : null);
            }
            finally { gate.Release(); }
        }, cancellationToken);
    }
    /// <summary>Reads one bounded projected page from an already-open borrowed connection. Query cancellation is cooperative.</summary>
    public Task<PageResult> ReadPageAsync(string name, PageRequest? request = null, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadPageCoreAsync(name, request, Options, cancellationToken), cancellationToken);
    /// <summary>Explicitly requests larger bounded previews, without changing session defaults.</summary>
    public Task<PageResult> ReadExpandedPageAsync(string name, PageRequest request, int textLength, int blobLength, CancellationToken cancellationToken = default)
    {
        var expanded = Options with { TextPreviewLength = textLength, BlobPreviewLength = blobLength }; expanded.Validate();
        return Task.Run(() => ReadPageCoreAsync(name, request, expanded, cancellationToken), cancellationToken);
    }
    private async Task<PageResult> ReadPageCoreAsync(string name, PageRequest? request, SqliteInspectionOptions readOptions, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadPageLockedAsync(name, request, readOptions, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private async Task<PageResult> ReadPageLockedAsync(string name, PageRequest? request, SqliteInspectionOptions readOptions, CancellationToken cancellationToken)
    {
        var table = Verified(name); request ??= new();
        var query = SqliteQueryBuilder.BuildPage(table, request, readOptions);
        using var command = Command(query.Text);
        foreach (var p in query.Parameters) command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        using var cancel = cancellationToken.Register(command.Cancel);
        var watch = Stopwatch.StartNew();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = ImmutableArray.CreateBuilder<ImmutableArray<DisplayNode>>();
        var rowKeys = ImmutableArray.CreateBuilder<ImmutableDictionary<string, object?>?>();
        var more = false; var truncated = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.Count == readOptions.PageSize) { more = true; break; }
            var cells = ImmutableArray.CreateBuilder<DisplayNode>();
            var keys = ImmutableDictionary.CreateBuilder<string, object?>();
            for (int c = 0; c < query.Columns.Length; c++)
            {
                var kind = reader.GetString(c * 3 + 1);
                long? length = reader.IsDBNull(c * 3 + 2) ? null : reader.GetInt64(c * 3 + 2);
                DisplayNode node;
                if (kind == "redacted") node = new PlaceholderNode(PlaceholderReason.Redacted);
                else if (kind == "null") node = new ScalarNode("null", "NULL");
                else if (kind == "blob")
                {
                    var bytes = (byte[])reader.GetValue(c * 3);
                    var cut = length > bytes.Length; truncated |= cut;
                    node = new ScalarNode(Convert.ToHexString(bytes), "BLOB length=" + length, false, cut);
                }
                else
                {
                    var text = Convert.ToString(reader.GetValue(c * 3), CultureInfo.InvariantCulture) ?? "";
                    var cut = text.Length > readOptions.TextPreviewLength || length > readOptions.TextPreviewLength; truncated |= cut;
                    text = DisplayText.Truncate(text, readOptions.TextPreviewLength);
                    text = readOptions.SensitiveData?.Sanitize(text) ?? text;
                    node = new ScalarNode(text, kind + (length is null ? "" : " length=" + length), kind == "text", cut);
                }
                if (c < query.VisibleColumnCount) cells.Add(node);
                if (query.Columns[c].PrimaryKeyOrdinal > 0 && node is ScalarNode { Truncated: false } && kind != "null") keys[query.Columns[c].Name] = reader.GetValue(c * 3);
            }
            rows.Add(cells.ToImmutable());
            var pkCount = table.Columns.Count(c => c.PrimaryKeyOrdinal > 0);
            rowKeys.Add(pkCount > 0 && keys.Count == pkCount ? keys.ToImmutable() : null);
        }
        var diagnostics = new QueryDiagnostics("Page", watch.Elapsed, rows.Count, truncated || more, query.Ordering, readOptions.PageSize);
        LastOperation = diagnostics;
        var total = GetCachedCount(name);
        var model = new TableNode(name, query.Columns.Take(query.VisibleColumnCount).Select(c => new TableColumn(c.Name, c.DeclaredType)).ToImmutableArray(), rows.ToImmutable(),
            request.Filter is null && request.AdditionalFilters.IsDefaultOrEmpty ? total : null, null, table.Columns.Count(c => c.Hidden != 1) - query.VisibleColumnCount,
            more ? new PlaceholderNode(PlaceholderReason.MoreItemsMayExist) : null);
        return new PageResult(model, diagnostics, more) { RecordKeys = rowKeys.ToImmutable(), Owner = this, SourceName = name };

    }
}

