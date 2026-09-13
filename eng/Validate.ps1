param([switch]$Clean)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    New-Item -ItemType Directory artifacts/logs -Force | Out-Null
    if($Clean) {
        foreach($parent in @('src','tests','samples','eng')) {
            $targets=Get-ChildItem -LiteralPath (Join-Path $root $parent) -Directory -Recurse | Where-Object Name -in @('bin','obj')
            foreach($target in $targets) {
                $resolved=[IO.Path]::GetFullPath($target.FullName)
                if(!$resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar) -or $target.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {throw 'Unsafe clean target'}
                Write-Host "Removing $resolved"
                Remove-Item -LiteralPath $resolved -Recurse -Force
            }
        }
    }
    foreach($command in @(@('--info'),@('restore'),@('build','-c','Release'),@('test','-c','Release'),@('pack','-c','Release'))) {
        $name=$command[0].TrimStart('-')
        & dotnet @command 2>&1 | Tee-Object -FilePath "artifacts/logs/$name.log"
        if($LASTEXITCODE -ne 0) {throw "dotnet $name failed: $LASTEXITCODE"}
    }
    & dotnet run --project eng/PackageAudit -c Release -- $root 2>&1 | Tee-Object artifacts/logs/package-audit.log
    if($LASTEXITCODE -ne 0) {throw 'Package audit failed'}
} finally {Pop-Location}
