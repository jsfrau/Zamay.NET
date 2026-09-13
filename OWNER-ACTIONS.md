# Owner actions

## Before publishing

- The public source repository is https://github.com/jsfrau/Zamay.NET; RepositoryUrl is centralized in eng/Release.props and PackageProjectUrl follows it.
- Make the release source commit public, rebuild from that commit, and run the package audit with --verify-urls before uploading.
- Publish the matching nupkg and snupkg using PUBLISHING.md after validation passes.

MIT and Authors=ZamaySolves were explicitly confirmed. They do not require another decision. An icon, documentation, packaging, tests, and release workflows are included.

## Optional automated publishing

Configure a NuGet Trusted Publishing policy for the real repository and release.yml, environment nuget, and set NUGET_USER in GitHub repository secrets to the NuGet profile name. Protect the nuget environment with an owner approval. Manual web upload does not require this setup.
