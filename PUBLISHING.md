# Publishing Zamay 2.0.0

The package is artifacts/packages/Zamay.2.0.0.nupkg. Symbols are in the adjacent Zamay.2.0.0.snupkg. The public source repository is https://github.com/jsfrau/Zamay.NET. Complete the validation and Source Link checks below before publishing a build.

## Manual NuGet.org upload

1. Set the actual public RepositoryUrl in eng/Release.props, make the release commit available there, and run the clean release validation and package consumer tests. Verify the repository link and Source Link report.
2. Sign in at https://www.nuget.org using an account that owns the existing Zamay package.
3. Open Upload: https://www.nuget.org/packages/manage/upload .
4. Select artifacts/packages/Zamay.2.0.0.nupkg.
5. Review the description, Authors=ZamaySolves, MIT license, icon, repository URL, and release notes.
6. Preview the embedded README and check its code formatting.
7. Verify Package ID is exactly Zamay and version is exactly 2.0.0.
8. Verify net8.0 and net8.0-windows7.0 assets, Microsoft.Data.Sqlite dependencies, and the Windows Forms framework reference only on the Windows target.
9. Submit when all metadata is correct. This is the irreversible publication step. NuGet versions cannot be replaced with different bytes.

The web upload is sufficient for the main package. Publish the matching .snupkg using the CLI procedure below if symbol upload is not offered by the portal. Use the exact matching build; do not rebuild between package and symbol publication.

## CLI

After completing the same checks, set NUGET_API_KEY in your own shell without writing it into a file or command history. Use a short-lived, narrowly scoped credential for Zamay.

```powershell
dotnet nuget push artifacts/packages/Zamay.2.0.0.nupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --no-symbols
dotnet nuget push artifacts/packages/Zamay.2.0.0.snupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

Those commands are instructions for the owner; they are never executed by the validation scripts. Confirm package status in your NuGet account after submission.

## GitHub Actions

ci.yml restores, builds, tests, audits, and packs; it never publishes. release.yml runs only through workflow_dispatch for tag v2.0.0 and uses the protected nuget environment. It validates and tests the package before requesting publication credentials.

To configure NuGet Trusted Publishing:

1. Create the real GitHub repository and commit the release configuration with its actual URL.
2. In NuGet.org, open your profile's Trusted Publishing page and add a GitHub policy for the actual repository owner and repository name.
3. Use workflow filename release.yml, not its .github/workflows path, and environment nuget.
4. Set the policy's package scope to Zamay and choose the account or organization that owns the existing package.
5. In GitHub, configure environment nuget with required owner approval, and store the NuGet profile name as NUGET_USER. No long-lived API key is required for this flow.
6. Make v2.0.0 point to the reviewed source commit. Dispatch the release workflow for that tag and approve the environment only after reviewing its checks.

The workflow uses NuGet/login@v1 with id-token: write to obtain a short-lived key. Account-side setup cannot be completed from a local checkout. Official instructions: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing . Manual submission guidance: https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package .
