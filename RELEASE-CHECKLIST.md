# Release checklist — Zamay 2.0.0

- [x] PackageId is exactly Zamay.
- [x] Version is exactly 2.0.0.
- [x] Old 1.x compatibility is intentionally not provided.
- [x] Public namespace and assembly names reviewed.
- [x] English README and detailed documentation prepared.
- [x] Description and tags reviewed.
- [x] Authors preserved from the published nuspec: ZamaySolves.
- [x] MIT explicitly confirmed by the owner; LICENSE included.
- [x] Real release RepositoryUrl configured in eng/Release.props.
- [x] Neutral PNG icon prepared.
- [x] Changelog, migration notice, and release notes prepared.
- [x] XML documentation generation and portable PDB configured.
- [x] Final clean restore/build/test/pack passes.
- [x] README, icon, license, XML, dependencies and assemblies inspected in nupkg.
- [x] snupkg generated and portable PDB/embedded sources inspected.
- [ ] Remote Source Link verified against the real published commit.
- [x] Console/SQLite local package consumer passes without ProjectReference.
- [x] Windows package consumer compiles and smoke-runs.
- [x] Documentation C# examples compile against the package.
- [x] Own source/docs/package reviewed for secrets, absolute paths and development artifacts.
- [x] Final package hashes recorded.
- [ ] Ready for NuGet.org upload.

The unchecked repository/remote Source Link items are owner prerequisites. Validation scripts do not publish. The release workflow rejects an unset RepositoryUrl before requesting publication credentials.

