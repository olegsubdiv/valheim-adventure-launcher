param(
    [ValidateSet('Status','Configure','UploadAndVerify')][string]$Action = 'Status',
    [string]$RemoteName = 'arpg-drive',
    [string]$SourceFile,
    [string]$RemoteFolder = 'ValheimAdventureReleases',
    [string]$PublisherHome = (Join-Path $env:LOCALAPPDATA 'ValheimAdventurePublisher'),
    [switch]$SkipDownloadVerify,
    [switch]$UseSharedClient,
    [string]$ClientId
)
$ErrorActionPreference = 'Stop'
if ($RemoteName -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,63}$') { throw 'Invalid remote name.' }
$toolRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools\rclone'
$rclone = Join-Path $toolRoot 'rclone-v1.75.2-windows-amd64\rclone.exe'
if (-not (Test-Path -LiteralPath $rclone -PathType Leaf)) { & (Join-Path $PSScriptRoot 'Install-Rclone.ps1') }
if (-not (Test-Path -LiteralPath $rclone -PathType Leaf)) { throw 'Portable rclone installation failed.' }
New-Item -ItemType Directory -Path $PublisherHome -Force | Out-Null
$config = Join-Path $PublisherHome 'rclone.conf'
function Invoke-Rclone([string[]]$Arguments) {
    $out = & $rclone @Arguments --config $config
    if ($LASTEXITCODE -ne 0) { throw "rclone operation failed (exit $LASTEXITCODE)." }
    return $out
}
function Get-RemoteNames {
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { return @() }
    return @(Invoke-Rclone -Arguments @('listremotes'))
}
if ($Action -eq 'Status') {
    $names = @(Get-RemoteNames)
    [pscustomobject]@{ tool='rclone v1.75.2'; configuration=$config; configured=($names -contains ($RemoteName+':')); remotes=$names } | ConvertTo-Json -Depth 3
    exit 0
}
if ($Action -eq 'Configure') {
    if ((Get-RemoteNames) -contains ($RemoteName+':')) {
        Write-Host "Remote $RemoteName already exists. Nothing changed."
        exit 0
    }
    Write-Host 'Google authorization is separate from the chat connector. Credentials stay in your local publisher configuration.'
    Write-Host 'This remote uses drive.file: it can access only files/folders it creates.'
    if (-not $ClientId -and -not $UseSharedClient) {
        Write-Host 'The shared rclone Google client is being retired during 2026.'
        Write-Host 'Recommended: create a Desktop OAuth client following https://rclone.org/drive/#making-your-own-client-id'
        $ClientId = Read-Host 'Your Desktop OAuth client ID, or leave blank to try the shared client'
        if (-not $ClientId) {
            $answer=Read-Host 'Try the retiring shared client for this test? [y/N]'
            if ($answer -notmatch '^(y|yes)$') { Write-Host 'Configuration deferred. No remote was created.'; exit 2 }
            $UseSharedClient=$true
        }
    }
    $argsList = @('config','create',$RemoteName,'drive','scope','drive.file','config_is_local','true','config_shared_client_id','true','--auto-confirm')
    if ($ClientId) {
        $secret=Read-Host 'Desktop OAuth client secret' -AsSecureString
        $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        try { $plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
        # The values are private local configuration, never a release or application setting.
        $argsList += @('client_id',$ClientId,'client_secret',$plain)
    }
    $privateOutput=Join-Path $PublisherHome ('configure-'+[Guid]::NewGuid().ToString('N')+'.private')
    try {
        Write-Host 'A browser will open for Google sign-in. Complete it there; no password is entered into this script.'
        # config create prints the resulting token, so its stdout must never be displayed or logged.
        & $rclone @argsList --config $config 1> $privateOutput
        $code=$LASTEXITCODE
        if ($code -ne 0) { throw "Google authorization failed (exit $code). If the shared client was rejected, use your own Desktop OAuth client." }
        if ((Get-RemoteNames) -notcontains ($RemoteName+':')) { throw 'Authorization did not create the requested remote.' }
        Write-Host "Google Drive publisher remote $RemoteName is ready. Public sharing has not been enabled."
    } finally {
        $plain=$null
        if (Test-Path -LiteralPath $privateOutput) { Remove-Item -LiteralPath $privateOutput -Force }
    }
    exit 0
}
if ((Get-RemoteNames) -notcontains ($RemoteName+':')) { throw 'Google Drive is not authorized yet. Run Configure-Drive.cmd first.' }
if (-not $SourceFile -or -not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) { throw 'Provide -SourceFile with an existing local file.' }
if ($RemoteFolder -match '(^/|:|\\|(^|/)\.\.(/|$))') { throw 'RemoteFolder must be a safe relative Drive folder.' }
$file=Get-Item -LiteralPath $SourceFile
if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source file must not be a link.' }
$sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$md5=(Get-FileHash -LiteralPath $file.FullName -Algorithm MD5).Hash.ToLowerInvariant()
$remotePath=$RemoteName+':'+$RemoteFolder.Trim('/')+'/'+$sha.Substring(0,16)+'-'+$file.Name
Write-Host ('Uploading '+$file.Name+' ('+$file.Length+' bytes).')
Invoke-Rclone -Arguments @('copyto',$file.FullName,$remotePath,'--immutable','--checksum','--transfers','1','--drive-chunk-size','64M','--stats','15s','--stats-one-line','--stats-log-level','NOTICE') | Out-Null
$info=(Invoke-Rclone -Arguments @('lsjson',$remotePath,'--stat','--hash') | Out-String) | ConvertFrom-Json
if ([long]$info.Size -ne $file.Length) { throw 'Uploaded file size does not match.' }
$remoteMd5=$info.Hashes.MD5
if (-not $remoteMd5 -or $remoteMd5.ToLowerInvariant() -ne $md5) { throw 'Drive MD5 checksum does not match.' }
$downloadVerified=$false
if (-not $SkipDownloadVerify) {
    Write-Host 'Streaming the uploaded file back for independent SHA256 verification.'
    $remoteHashOutput=Invoke-Rclone -Arguments @('hashsum','SHA-256',$remotePath,'--download') | Out-String
    $downloadSha=($remoteHashOutput.Trim() -split '\s+')[0].ToLowerInvariant()
    if ($downloadSha -ne $sha) { throw 'Downloaded SHA256 does not match.' }
    $downloadVerified=$true
}
$result=[pscustomobject]@{name=$file.Name;bytes=$file.Length;sha256=$sha;driveMd5Verified=$true;downloadSha256Verified=$downloadVerified;remotePath=$remotePath;url=('https://drive.google.com/file/d/'+$info.ID+'/view');downloadUrl=('https://drive.google.com/uc?export=download&id='+$info.ID);publicSharingChanged=$false}
$result | ConvertTo-Json -Depth 3
