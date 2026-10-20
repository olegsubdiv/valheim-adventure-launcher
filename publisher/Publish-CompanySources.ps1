[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourcesFile,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$private=Join-Path $repo '.private'
$codeFile=Join-Path $private 'company-code.txt'
$payloadPath=Join-Path $private 'company-sources.payload.json'
$signedPath=Join-Path $private 'company-sources.signed.json'
$encryptedPath=Join-Path $private 'company-sources.enc.json'
$dotnet=Join-Path $repo '.tools\dotnet\dotnet.exe'
$cli=Join-Path $repo 'src\AdventureLauncher.Cli\bin\Release\net10.0\AdventureLauncher.Cli.dll'
$publicKey=Join-Path $repo 'public-key.pem'
function Invoke-Cli([string[]]$Arguments) {
    & $dotnet $cli @Arguments
    if($LASTEXITCODE -ne 0){throw 'Company publisher CLI failed.'}
}
Push-Location $repo
try {
    & $dotnet build (Join-Path $repo 'src\AdventureLauncher.Cli') -c Release --nologo
    if($LASTEXITCODE -ne 0){throw 'CLI build failed.'}
    Invoke-Cli @('verify','--manifest',(Join-Path $repo 'channel\stable.json'),'--key',$publicKey)
    $envelope=Get-Content (Join-Path $repo 'channel\stable.json') -Raw | ConvertFrom-Json
    $pack=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.payload)) | ConvertFrom-Json
    $sources=@(Get-Content -LiteralPath $SourcesFile -Raw | ConvertFrom-Json)
    if($sources.Count -ne 2){throw 'Expected the two explicitly selected HD and shader archives.'}
    $allowed=@('Badger-ShadingOverhaul-0.6.0','Willybach-HDValheim_Higher-2.0.0')
    $artifacts=@()
    foreach($source in $sources) {
        if($source.id -notin $allowed){throw 'Unexpected company archive.'}
        $artifact=@($pack.artifacts | Where-Object {$_.id -eq $source.id})
        if($artifact.Count -ne 1 -or $artifact[0].size -ne $source.size -or $artifact[0].sha256 -ne $source.sha256){throw 'Company archive pin does not match the signed collection.'}
        if(-not $source.driveMd5Verified){throw 'The Drive upload has not been verified.'}
        $artifact[0].urls=@($source.downloadUrl)
        $artifact[0].manualUrl=$null
        $artifact[0].instructions=$null
        $artifacts+= $artifact[0]
    }
    if(@($artifacts.id | Sort-Object -Unique).Count -ne 2){throw 'Duplicate company archive.'}
    $payload=[ordered]@{schemaVersion=1;packId='adventure-rpg-company-sources';title='Company archive sources';version=([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'));publishedAt=[DateTimeOffset]::UtcNow.ToString('o');artifacts=$artifacts;files=@()}
    [IO.File]::WriteAllText($payloadPath,($payload | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    if(-not(Test-Path -LiteralPath $codeFile)){Invoke-Cli @('company-code','--out',$codeFile)}
    Invoke-Cli @('sign','--manifest',$payloadPath,'--key',(Join-Path $private 'manifest-signing.pem'),'--out',$signedPath)
    Invoke-Cli @('seal-sources','--manifest',$signedPath,'--key',$publicKey,'--code-file',$codeFile,'--out',$encryptedPath)
    Invoke-Cli @('check-sources','--encrypted',$encryptedPath,'--key',$publicKey,'--code-file',$codeFile,'--base-manifest',(Join-Path $repo 'channel\stable.json'))
    if(-not $Publish){Write-Host 'Encrypted list prepared locally. The code and decrypted links remain in .private.';exit 0}
    if(@(git diff --cached --name-only).Count -gt 0){throw 'Commit unrelated staged changes before publishing the encrypted source list.'}
    Copy-Item -LiteralPath $encryptedPath -Destination (Join-Path $repo 'channel\company-sources.enc.json') -Force
    git add -- channel/company-sources.enc.json
    if($LASTEXITCODE -ne 0){throw 'Could not stage encrypted source list.'}
    git commit -m 'Publish encrypted company archive sources'
    if($LASTEXITCODE -ne 0){throw 'Could not commit encrypted source list.'}
    git push origin HEAD:main
    if($LASTEXITCODE -ne 0){throw 'Could not publish encrypted source list.'}
    Write-Host 'Encrypted source list published. Share the local company-code.txt only with the company.'
}
finally {Pop-Location}
