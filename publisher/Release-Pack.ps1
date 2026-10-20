[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9][A-Za-z0-9._-]{0,63}$')][string]$Version,
    [Parameter(Mandatory)][string]$ClientRoot,
    [string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$Repository = 'olegsubdiv/valheim-adventure-launcher',
    [string]$LiveProfile,
    [string]$ServerConfigRoot,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {throw 'Invalid GitHub repository.'}
if([bool]$LiveProfile -ne [bool]$ServerConfigRoot) {throw 'Provide both -LiveProfile and -ServerConfigRoot, or neither for a prepared snapshot.'}
$dotnet=Join-Path $repo '.tools\dotnet\dotnet.exe'
if(-not (Test-Path -LiteralPath $dotnet)) {$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_ROOT=Split-Path -Parent $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$python=(Get-Command python -ErrorAction Stop).Source
$tag='content-'+$Version
$output=Join-Path $repo ('artifacts\pack\'+$Version)
if($Publish) {
    & gh release view $tag --repo $Repository --json tagName 1>$null 2>$null
    if($LASTEXITCODE -eq 0) {throw 'This release version already exists. Use a new version; published versions are immutable.'}
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$argsList=@((Join-Path $PSScriptRoot 'build_manifest.py'),'--project-root',$ProjectRoot,'--client-root',$ClientRoot,'--output',$output,'--version',$Version,'--artifact-base-url',('https://github.com/'+$Repository+'/releases/download/'+$tag))
if($LiveProfile) {$argsList+=@('--live-profile',$LiveProfile,'--server-config-root',$ServerConfigRoot)}
& $python @argsList
if($LASTEXITCODE -ne 0) {throw 'Release inventory generation failed.'}
$payload=Join-Path $output 'manifest.payload.json'
$signed=Join-Path $output 'stable.json'
$key=Join-Path $repo '.private\manifest-signing.pem'
if(-not (Test-Path -LiteralPath $key)) {throw 'Local manifest signing key is missing. Do not generate a replacement for an existing launcher trust key.'}
& $dotnet run --project (Join-Path $repo 'src\AdventureLauncher.Cli') -c Release -- sign --manifest $payload --key $key --out $signed
if($LASTEXITCODE -ne 0) {throw 'Manifest signing failed.'}
& $dotnet run --project (Join-Path $repo 'src\AdventureLauncher.Cli') -c Release -- verify --manifest $signed --key (Join-Path $repo 'public-key.pem')
if($LASTEXITCODE -ne 0) {throw 'Manifest verification failed.'}
if(-not $Publish) {
    Write-Host ('Prepared release: '+$output)
    Write-Host 'No files were uploaded. Run again with -Publish to publish this version.'
    exit 0
}
Push-Location $repo
try {
    $notes=Join-Path $output 'release-notes.txt'
    $noteText='Pinned Valheim Adventure RPG collection '+$Version+'.'+[Environment]::NewLine+[Environment]::NewLine+'The launcher installs exact mod versions and the signed configuration snapshot. Existing matching files are reused. Original Nexus archives are imported when needed.'
    [IO.File]::WriteAllText($notes,$noteText,[Text.UTF8Encoding]::new($false))
    & gh release create $tag --repo $Repository --prerelease --target main --title ('Collection '+$Version) --notes-file $notes
    if($LASTEXITCODE -ne 0) {throw 'Creating the content release failed.'}
    $zip=Join-Path $output ('adventure-configs-'+$Version+'.zip')
    & gh release upload $tag $zip $signed --repo $Repository
    if($LASTEXITCODE -ne 0) {throw 'Uploading the release assets failed. The stable channel was not changed.'}
    $verify=Join-Path $output 'remote-verification'
    New-Item -ItemType Directory -Path $verify -Force | Out-Null
    & gh release download $tag --repo $Repository --pattern ('adventure-configs-'+$Version+'.zip') --pattern 'stable.json' --dir $verify
    if($LASTEXITCODE -ne 0) {throw 'Published assets could not be downloaded for verification. The stable channel was not changed.'}
    foreach($source in @($zip,$signed)) {
        $download=Join-Path $verify ([IO.Path]::GetFileName($source))
        if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $download).Hash) {throw 'Published asset checksum mismatch. The stable channel was not changed.'}
    }
    $staged=@(git diff --cached --name-only)
    if($staged.Count -gt 0) {throw 'The repository has staged changes. Release assets are uploaded, but the channel was not changed.'}
    $channel=Join-Path $repo 'channel'
    New-Item -ItemType Directory -Path $channel -Force | Out-Null
    Copy-Item -LiteralPath $signed -Destination (Join-Path $channel 'stable.json') -Force
    git add -- channel/stable.json
    if($LASTEXITCODE -ne 0) {throw 'Could not stage the channel manifest.'}
    git commit -m ('Release collection '+$Version)
    if($LASTEXITCODE -ne 0) {throw 'Could not commit the channel manifest.'}
    git push origin HEAD:main
    if($LASTEXITCODE -ne 0) {throw 'Channel push failed. Local signed manifest and uploaded assets are retained.'}
    Write-Host ('Published collection '+$Version+'. Clients will see it on their next update.')
}
finally {Pop-Location}
