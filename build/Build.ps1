[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0',
    [string]$DotNet,
    [switch]$SkipTests,
    [switch]$SkipPack
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $DotNet) {
    $local = Join-Path $repo '.tools\dotnet\dotnet.exe'
    $DotNet = if (Test-Path -LiteralPath $local) { $local } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$env:DOTNET_ROOT = Split-Path -Parent $DotNet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Invoke-DotNet([string[]]$Arguments) {
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet exited with code $LASTEXITCODE" }
}
Push-Location $repo
try {
    $desktop = Join-Path $repo 'src\AdventureLauncher.Desktop\AdventureLauncher.Desktop.csproj'
    if (-not $SkipTests) {
        foreach ($name in @('AdventureLauncher.Core.Tests','AdventureLauncher.Client.Tests')) {
            $testProject = Join-Path $repo ("tests\$name\$name.csproj")
            if (-not (Test-Path -LiteralPath $testProject)) { throw "$name regression project is missing. Use -SkipTests only for a development build." }
            Invoke-DotNet @('run','--project',$testProject,'--configuration','Release')
        }
    }
    $buildRoot = Join-Path $repo ('artifacts\build\' + $Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    $publish = Join-Path $buildRoot 'app'
    $releases = Join-Path $repo 'artifacts\releases'
    $packageOutput = Join-Path $buildRoot 'releases'
    New-Item -ItemType Directory -Path $publish -Force | Out-Null
    New-Item -ItemType Directory -Path $releases -Force | Out-Null
    Invoke-DotNet @('publish',$desktop,'--configuration','Release','--runtime','win-x64','--self-contained','true','--output',$publish,('-p:Version='+$Version))
    $smokeReport = Join-Path $buildRoot 'desktop-self-test.txt'
    $process = Start-Process -FilePath (Join-Path $publish 'AdventureLauncher.exe') -ArgumentList @('--self-test',('"'+$smokeReport+'"')) -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeReport)) { throw "Desktop self-test failed (exit $($process.ExitCode))." }
    if (-not $SkipPack) {
        $toolPath = Join-Path $repo '.tools\velopack'
        $vpk = Join-Path $toolPath 'vpk.exe'
        if (-not (Test-Path -LiteralPath $vpk)) { Invoke-DotNet @('tool','install','vpk','--version','1.2.161','--tool-path',$toolPath) }
        & $vpk pack --packId 'OlegSubdiv.ValheimAdventure' --packVersion $Version --packDir $publish --mainExe 'AdventureLauncher.exe' --packTitle 'Valheim Adventure RPG' --packAuthors 'OlegSubdiv' --outputDir $packageOutput --runtime 'win-x64' --shortcuts 'Desktop,StartMenuRoot'
        if ($LASTEXITCODE -ne 0) { throw "Velopack exited with code $LASTEXITCODE" }
    }
    if (-not $SkipPack) {
        foreach ($asset in Get-ChildItem -LiteralPath $packageOutput -File) {
            Copy-Item -LiteralPath $asset.FullName -Destination (Join-Path $releases $asset.Name) -Force
        }
    }
    $outputs = if ($SkipPack) { @() } else { @(Get-ChildItem -LiteralPath $packageOutput -File | ForEach-Object {
        [ordered]@{ name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }) }
    [ordered]@{
        version=$Version; created_at=[DateTimeOffset]::UtcNow.ToString('o')
        tests_run=(-not $SkipTests); packaged=(-not $SkipPack)
        self_contained=$true; runtime='win-x64'; velopack_version='1.2.161'
        publish_directory=$publish; releases_directory=$packageOutput; desktop_self_test=$smokeReport; files=$outputs
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $buildRoot 'build-receipt.json') -Encoding UTF8
    Write-Host "Built launcher $Version"
    Write-Host "App: $publish"
    if (-not $SkipPack) { Write-Host "Installer/update assets: $releases" }
}
finally { Pop-Location }
