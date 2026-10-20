$ErrorActionPreference='Stop'
$version='v1.75.2'
$expected='8a9db2bad1dc79a65b3348fd857069598e2aab7cf4d118b77b1df23055aff818'
$target=Join-Path (Split-Path $PSScriptRoot -Parent) '.tools\rclone'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$name='rclone-'+$version+'-windows-amd64.zip'
$archive=Join-Path $target $name
$exe=Join-Path $target ('rclone-'+$version+'-windows-amd64\rclone.exe')
if (Test-Path -LiteralPath $exe) { Write-Host 'Portable rclone is already installed.'; exit 0 }
Invoke-WebRequest -UseBasicParsing -Uri ('https://downloads.rclone.org/'+$version+'/'+$name) -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Official rclone archive SHA256 mismatch; not extracting.' }
Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
Write-Host ('Installed portable rclone '+$version)
