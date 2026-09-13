param([switch]$SkipWindows,[switch]$SkipReadme)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$smokeRoot=Join-Path $root ('artifacts/package-smoke/'+[Guid]::NewGuid().ToString('N'))
$feed=Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory $smokeRoot -Force | Out-Null
$oldPackages=$env:NUGET_PACKAGES
$env:NUGET_PACKAGES=Join-Path $smokeRoot '.packages'
function Invoke-Dotnet([string[]]$Arguments,[string]$Log) {
    & dotnet @Arguments 2>&1 | Out-File -LiteralPath $Log
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $Log;throw "dotnet $($Arguments -join ' ') failed"}
}
function New-Consumer([string]$Name,[string]$Code,[bool]$Windows,[bool]$Run) {
    $path=Join-Path $smokeRoot $Name
    New-Item -ItemType Directory $path -Force | Out-Null
    $properties=if($Windows){'<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms>'}else{'<TargetFramework>net8.0</TargetFramework>'}
    "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType>$properties<IsPackable>false</IsPackable></PropertyGroup></Project>" | Set-Content (Join-Path $path "$Name.csproj")
    $Code | Set-Content (Join-Path $path 'Program.cs')
    $escaped=[Security.SecurityElement]::Escape($feed)
    @"
<configuration>
 <packageSources><clear/><add key="local" value="$escaped"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
 <packageSourceMapping><packageSource key="local"><package pattern="Zamay"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content (Join-Path $path 'NuGet.Config')
    Push-Location $path
    try {
        Invoke-Dotnet @('add','package','Zamay','--version','2.0.0','--source',$feed,'--no-restore') (Join-Path $path 'add.log')
        if((Get-Content "$Name.csproj" -Raw).Contains('ProjectReference')){throw 'Consumer has ProjectReference'}
        Invoke-Dotnet @('restore') (Join-Path $path 'restore.log')
        Invoke-Dotnet @('build','-c','Release','--no-restore') (Join-Path $path 'build.log')
        if($Run){Invoke-Dotnet @('run','-c','Release','--no-build') (Join-Path $path 'run.log')}
        Write-Host "$Name package consumer: PASS (run=$Run)"
    } finally {Pop-Location}
}
try {
    New-Consumer 'ConsoleSqlite' (Get-Content (Join-Path $PSScriptRoot 'Consumer.Console.cs.txt') -Raw) $false $true
    if(!$SkipWindows){New-Consumer 'Windows' (Get-Content (Join-Path $PSScriptRoot 'Consumer.Windows.cs.txt') -Raw) $true $true}
    if(!$SkipReadme) {
        $index=0
        foreach($doc in @('README.md','docs/custom-inspectors.md','docs/migration-2.0.md')) {
            $text=Get-Content (Join-Path $root $doc) -Raw
            foreach($match in [regex]::Matches($text,'(?s)```csharp\s*\r?\n(.*?)```')) {
                $index++;$code=$match.Groups[1].Value
                $windows=$code.Contains('ToMessageBox(')
                if($windows -and $SkipWindows){continue}
                if($windows){$code="using System;`n"+$code}
                New-Consumer "Example$index" $code $windows (!$windows)
            }
        }
    }
    "Local package consumers passed. Run directory: $([IO.Path]::GetRelativePath($root,$smokeRoot))`nWindows skipped: $SkipWindows; documentation examples skipped: $SkipReadme" | Set-Content (Join-Path $root 'artifacts/consumer-results.txt')
} finally {$env:NUGET_PACKAGES=$oldPackages}


