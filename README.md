# Zamay

Zamay is a diagnostic object and data inspector for .NET. Use it to examine objects, collections, tables, exceptions, and SQLite databases during development.

```csharp
using Zamay;

new { Id = 42, Name = "Алексей", Active = true }.ToConsole();
```

## Install

```sh
dotnet add package Zamay --version 2.0.0
```

One package provides console and SQLite inspection on .NET 8, plus a diagnostic window for Windows applications targeting `net8.0-windows` with `UseWindowsForms=true`. NuGet selects the appropriate `Zamay.dll`; the `net8.0` assembly does not reference Windows Forms.

Zamay 2.0 is a complete rewrite of the original package and is not API-compatible with 1.x.

## Quick start

```csharp
using Zamay;

var users = new[] {
    new { Id = 1, Name = "Алексей" },
    new { Id = 2, Name = "Мария" }
};
users.ToConsole();

var serviceState = new {
    Name = "Import service",
    Active = true,
    LastRun = DateTimeOffset.UtcNow,
    Users = users,
    Password = "not included in the output"
};
serviceState.ToConsole();
string text = serviceState.ToDisplayString();
```

Console output preserves Cyrillic and escapes control characters, including terminal escape sequences. Sensitive member names are checked before their getters run.

## Console and object inspection

`ToConsole` writes to `Console.Out` by default. Pass a `TextWriter` to capture output; Zamay does not dispose it. The convenience method does not append an extra newline.

```csharp
using Zamay;
using Zamay.Core;

var value = new { Id = 42, Name = "Алексей" };
using var writer = new StringWriter();
value.ToConsole(writer);

var inspector = new ObjectInspector();
DisplayDocument document = inspector.Inspect(value);
var renderer = new TextRenderer();
renderer.Render(document, Console.Out);
string text = renderer.RenderToString(document);
```

An inspection creates a materialized `DisplayDocument`. Text and Windows renderers use that same snapshot without revisiting the object. Reuse an `ObjectInspector` to reuse its type-resolution and reflection caches. Traversal state is isolated per call.

## Collections and tables

Arrays, lists, dictionaries, and other synchronous enumerables are inspected up to their limits. A cycle produces a reference node; a shared object in two independent branches is not mistaken for a cycle. `DataTable` becomes a table node, and `DataSet` contains table nodes.

```csharp
using System.Data;
using Zamay;

var table = new DataTable("Users");
table.Columns.Add("Id", typeof(int));
table.Columns.Add("Name", typeof(string));
table.Rows.Add(42, "Алексей");
table.ToConsole();
```

## Windows inspector

In a Windows Forms project:

```csharp
using Zamay;

var value = new { Id = 42, Name = "Алексей" };
value.ToMessageBox();
```

The name is a convenience API: it opens a custom Zamay diagnostic dialog, not a standard `System.Windows.Forms.MessageBox` containing a formatted dump. The dialog provides an object tree, a detail panel, a table grid, search, Copy, a text tab, and resizing.

From an existing form, pass `owner: this`. The owner must have a live handle. Calls are routed to its UI thread; calls without an owner can use a dedicated STA thread whose lifetime ends when the dialog closes. `ToMessageBoxAsync` also accepts cancellation. Do not synchronously block a UI thread while a worker is trying to invoke that same owner.

## SQLite inspector

The package supports `Microsoft.Data.Sqlite`. The caller opens and owns the connection:

```csharp
using Microsoft.Data.Sqlite;
using Zamay;

var connectionString = "Data Source=:memory:";
using var connection = new SqliteConnection(connectionString);
connection.Open();
connection.ToMessageBox();
```

The Windows browser loads schema metadata when it opens. It does not read every table or count every row. Select a table or view, choose columns, and request Browse, Latest, or an exact Count.

Pages use an explicit column projection. TEXT and BLOB values are previewed with limits applied in SQL before materialization. Record Details can reload a row by its complete primary key, including composite keys. Larger previews require an explicit request.

SQLite has no natural “latest” order. Latest uses a chosen column, a suitable INTEGER PRIMARY KEY, or an available rowid. The UI shows the actual ordering. A key order is not proof of insertion time; choose an application timestamp when that is what you need. If no suitable order is available, Zamay asks for a column instead of presenting unordered rows as latest.

The same session API is available without Windows:

```csharp
using Microsoft.Data.Sqlite;
using Zamay.Sqlite;

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
using (var command = connection.CreateCommand()) {
    command.CommandText = "CREATE TABLE Users(Id INTEGER PRIMARY KEY, Name TEXT)";
    command.ExecuteNonQuery();
}
var session = new SqliteInspectorSession(connection);
var schema = await session.LoadSchemaAsync();
var page = await session.ReadPageAsync("Users", new PageRequest {
    Columns = ["Id", "Name"], Latest = true
});
long count = await session.CountAsync("Users");
```

Zamay never opens, closes, or disposes a borrowed connection, and never changes its transaction. Avoid concurrent use of that connection outside the session. Exact counts are cached until a refresh; external writes may make them stale.

## Configuration and sensitive data

```csharp
using Zamay;
using Zamay.Core;

var value = new { Id = 42, PrivateValue = "hidden" };
var options = new InspectionOptions {
    MaxDepth = 4,
    MaxCollectionItems = 20,
    MaxStringLength = 500,
    SensitiveData = new SensitiveDataPolicy(["PrivateValue"])
};
value.ToConsole(options: options);
```

Independent budgets cover depth, node visits, collection items, object members, strings, text output, and byte previews. Invalid options throw `ArgumentOutOfRangeException`; options are not silently clamped. Limit markers are semantic document nodes. String truncation does not split UTF-16 surrogate pairs.

Default sensitive names include Password, Passwd, Secret, Token, ApiKey, Authorization, and ConnectionString, matched case-insensitively. Add names, implement `ISensitiveDataPolicy`, or explicitly set `SensitiveData=null` to disable masking. String sanitization is best-effort and does not discover every secret. A database connection string is never included automatically.

Exception policy can show the type only, type and message, or full details. Stack traces require a separate option. Messages are sanitized and truncated.

## Custom inspectors

Register `IValueInspector` implementations with `ObjectInspector`. Higher priority wins, and the chosen inspector is cached by type. Inspectors return document nodes; they do not write text. They are trusted application code and must respect masking and resource limits. For nested objects, check `CanInspectChild` before reading a getter and use `InspectChild` to preserve traversal budgets and cycle detection.

A complete example is included in `docs/custom-inspectors.md` and the console sample.

## Async inspection

Async sources remain descriptors unless root enumeration is explicitly enabled:

```csharp
using Zamay;
using Zamay.Core;

await Values().ToConsoleAsync(
    asyncOptions: new AsyncInspectionOptions {
        EnumerateAsyncEnumerable = true, MaxItems = 5
    });

static async IAsyncEnumerable<int> Values() {
    for (int i = 0; i < 10; i++) {
        await Task.Yield();
        yield return i;
    }
}
```

The limit is combined with the ordinary traversal budgets. Nested async sources are not enumerated. Tasks and ValueTasks are not awaited by the inspector.

## Safety and limits

Zamay is not a sandbox. Property getters can execute arbitrary application code, and `IEnumerable.MoveNext` can block. Cancellation is checked between traversal steps and cannot forcibly stop arbitrary synchronous code.

Streams are not read, `IDataReader` is not consumed, and delegates are not invoked. Reflection-based inspection has trimming and Native AOT limitations, marked by `RequiresUnreferencedCode` on entry points. Custom inspectors do not make arbitrary reflection AOT-compatible.

SQLite SELECT queries, views, generated expressions, sorting, and exact counts can still be expensive even when the returned page is small. Provider cancellation and command timeout are not hard deadlines. Paging does not create a transaction snapshot across requests.

## Documentation and platforms

The package includes detailed documentation under `docs/`: architecture, safety and budgets, Windows threading, SQLite behavior, custom inspectors, and migration from 1.x. The source tree also includes runnable console and Windows samples, tests, and publishing instructions.

Console and SQLite APIs target .NET 8. Windows APIs require Windows and the Windows Desktop runtime. Linux/macOS applications select the non-Windows assembly. The current release validation runs on Windows; CI includes a separate Linux console/SQLite job.

## License

MIT. The full license text is included as `LICENSE` in the package.
