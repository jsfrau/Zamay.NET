# Owner actions

## Zamay 2.0.0 status

- The public source repository is https://github.com/jsfrau/Zamay.NET; RepositoryUrl is centralized in eng/Release.props and PackageProjectUrl follows it.
- Source commit 84e91a2b3ed400f2b879290197ceee1f53a86c8a is public under tag v2.0.0. Source Link downloads and SHA256 checks passed for both frameworks.
- The main package and matching symbols package were submitted successfully to NuGet.org. Installation from the public NuGet feed was verified with a fresh package cache; Console and SQLite smoke checks passed. No owner actions remain for this release.
- GitHub Release includes both submitted package files and SHA256SUMS.txt: https://github.com/jsfrau/Zamay.NET/releases/tag/v2.0.0.

MIT and Authors=ZamaySolves were explicitly confirmed. They do not require another decision. An icon, documentation, packaging, tests, and release workflows are included.

## Optional automated publishing

Configure a NuGet Trusted Publishing policy for the real repository and release.yml, environment nuget, and set NUGET_USER in GitHub repository secrets to the NuGet profile name. Protect the nuget environment with an owner approval. Manual web upload does not require this setup.
