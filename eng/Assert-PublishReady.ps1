$ErrorActionPreference='Stop'
[xml]$settings=Get-Content (Join-Path $PSScriptRoot 'Release.props') -Raw
$url=[string]$settings.Project.PropertyGroup.RepositoryUrl
if([string]::IsNullOrWhiteSpace($url)) {throw 'Set the real public RepositoryUrl in eng/Release.props and rebuild before publishing.'}
if($url -notmatch '^https://github\.com/[^/]+/[^/]+/?$') {throw 'Review RepositoryUrl: an actual public GitHub repository is required for this workflow.'}
if($env:GITHUB_REPOSITORY -and $url.TrimEnd('/') -ne "https://github.com/$env:GITHUB_REPOSITORY") {throw 'RepositoryUrl does not match the workflow repository.'}
if([string]$settings.Project.PropertyGroup.ZamayVersion -ne '2.0.0') {throw 'Unexpected release version'}
Write-Host 'Release metadata guard passed. Package tests and Source Link audit are still required.'
