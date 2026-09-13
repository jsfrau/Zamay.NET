# Architecture

Inspection creates immutable DisplayDocument records. Scalar, object, collection, dictionary, table, error, reference, and placeholder nodes contain diagnostic data rather than rendering instructions. Renderers never revisit the source object.

ObjectInspector is reusable. TypeInspectorResolver caches custom inspector selection, including negative resolution, using ConcurrentDictionary and Lazy. ReflectionMetadataCache caches public instance member accessors by type. Each InspectionSession owns its path map, counters, options, and cancellation token. Reference equality is used only along the active traversal path.

The source solution keeps Core, Console, Sqlite, Presentation, Windows, and Windows.Sqlite modules separate to check dependency boundaries. Only src/Zamay is packable. It compiles those source files into Zamay.dll for net8.0 and net8.0-windows; Windows sources are excluded from net8.0. Module assemblies are not shipped, and consumers must not mix module ProjectReferences with the public package.

Both package targets depend on Microsoft.Data.Sqlite. NuGet restores the provider's native SQLite runtime assets transitively. Only the Windows target references Microsoft.WindowsDesktop.App.WindowsForms. Unit tests exercise the modules; separate consumers exercise the actual package without ProjectReference.
