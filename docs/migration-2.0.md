# Migrating from Zamay 1.x

Zamay 2.0 is a ground-up rewrite. The old ZamaySolves, ZamayLogic, OutputFunction<T>, and ToCustomOutput APIs were removed. There is no compatibility layer.

```csharp
using Zamay;

var value = new { Id = 42, Name = "Алексей" };
value.ToConsole();
string text = value.ToDisplayString();
```

For a Windows diagnostic window, target net8.0-windows, enable Windows Forms, and call value.ToMessageBox(). For an already-open Microsoft.Data.Sqlite connection, the same method opens the database browser.

Advanced inspection is in Zamay.Core, SQLite sessions in Zamay.Sqlite, and Windows dialogs in Zamay.Windows. The package identity remains Zamay; version 2.0.0 intentionally breaks source and binary compatibility with 1.x.
