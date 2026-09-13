# SQLite inspection

SqliteInspectorSession borrows an already-open Microsoft.Data.Sqlite.SqliteConnection and an optional SqliteTransaction. It does not open, close, dispose, begin, commit, or roll back caller resources. Calls within a session are serialized; the caller must avoid concurrent external use of the connection. Commands and readers are disposed before an operation returns.

The provider performs much of its work synchronously. Session operations use worker tasks to avoid blocking a UI message loop. Cancellation is cooperative; neither CancellationToken nor CommandTimeoutSeconds can guarantee a hard native query deadline.

LoadSchemaAsync reads main-schema tables, views, columns, primary keys, and index metadata. sqlite_% objects are hidden by default. Names are resolved against main explicitly, so temp tables cannot shadow a selected object. Attached and temp schemas are not browsed. MaxSchemaObjects defaults to 500; metadata is bounded to 2000 columns, 200 indexes, and 16000 definition characters per object. Truncation is indicated in the model.

CountAsync performs an explicit exact COUNT(*) and caches it until refresh. AutomaticCounts is off by default. Counts may be expensive and stale after external writes.

ReadPageAsync uses a verified explicit projection, quoted identifiers, and parameterized values. PageSize defaults to 50; MaxColumns to 64. At most one extra row is read to determine HasMore. Filters support comparisons, contains with literal wildcard escaping, and null predicates. UI filter values are strings; API values can be typed.

Latest uses a caller-selected order column, then a suitable INTEGER PRIMARY KEY alias, then an unshadowed rowid alias if available. WITHOUT ROWID and views do not get an invented rowid. INTEGER PRIMARY KEY DESC is not mistaken for the rowid alias. Primary-key components or an available rowid provide tie-breakers where possible. Without a unique tie-breaker, equal ordering values can still swap positions. Key order does not prove insertion time. Unordered browse is explicitly labelled; offset paging is not a stable snapshot during writes.

SQL substr limits TEXT and BLOB materialization. Defaults are 2000 UTF-16 units and 64 bytes. SQLite counts Unicode characters for TEXT substr, then a .NET limit prevents splitting a surrogate pair. BLOB diagnostics carry length and hexadecimal preview. ReadExpandedPageAsync explicitly requests larger bounded previews without mutating defaults.

ReadRecordAsync uses private complete, non-null, untruncated PK values retained from the page. A limited number of hidden key previews can be added to the projection. ReadRecordByPrimaryKeyAsync also accepts explicit typed keys, including composite keys. All known columns are fetched in bounded batches. Supply your own transaction if those batches must share a snapshot. Missing, masked, null, or truncated keys cannot silently identify another row; the UI falls back to the already-loaded projection with an explanation.

The browser's Record details button reloads by PK. A grid double-click only shows the currently loaded projection. Diagnostics include operation, elapsed time, row count, truncation, ordering, and page size, without parameter values or connection strings.

Result bounds do not bound SQLite CPU or I/O: views, virtual tables, user functions, generated columns, sorting, length(TEXT), COUNT, and large offsets may be expensive. Use a read-only connection when appropriate and process isolation for untrusted databases.
