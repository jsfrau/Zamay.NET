# Building and validation

The source solution uses .NET 8 and nullable reference types, with compiler warnings treated as errors. global.json chooses the .NET 8 SDK family. Install a Windows Desktop-capable .NET 8 SDK for the full solution.

```sh
dotnet --info
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release
```

Only src/Zamay/Zamay.csproj is packable. It produces artifacts/packages/Zamay.2.0.0.nupkg and Zamay.2.0.0.snupkg. Internal module projects remain separate for testing and are not package dependencies.

eng/Validate.ps1 -Clean removes bin and obj only from verified project directories inside this checkout, then runs the commands above. eng/Test-Package.ps1 creates isolated consumer projects and installs Zamay from the local package directory. Its package cache is local to the smoke run, so an earlier Zamay.2.0.0 cannot be mistaken for the new build. Consumers contain no ProjectReference.

eng/PackageAudit inspects nupkg entries, nuspec metadata, framework dependencies, XML documentation, assembly references, portable PDB documents, embedded sources, and Source Link metadata. RepositoryUrl is centralized in eng/Release.props and points to the public Zamay.NET source repository. Remote source validation requires the build's exact commit to be public.

The README C# blocks are compiled as separate consumers against the package. Window-opening examples are compile-only; dedicated Windows consumer smoke runs close their dialogs automatically. The Linux CI job runs Core/SQLite tests and the non-Windows consumer. Local validation on Windows does not claim a local Linux or macOS run.

After RepositoryUrl is set and the source commit is public, run: dotnet run --project eng/PackageAudit -c Release -- . --verify-urls . This mode downloads mapped source files and compares their SHA256 hashes to the portable PDB. Generated obj files use embedded sources and are excluded from remote checks. The release workflow requires this check before publication.

