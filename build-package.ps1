param([string]$ValheimPath = 'D:\spellbook\steam\steamapps\common\Valheim')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'PerspexModpackPatcher.csproj'
& dotnet build $project -c Release -p:ValheimPath=$ValheimPath -v:q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& dotnet build (Join-Path $root 'preloader\PerspexLegendsPreloader.csproj') -c Release -p:ValheimPath=$ValheimPath -v:q
if ($LASTEXITCODE -ne 0) { throw 'Preloader build failed' }

$stage = Join-Path $root 'release\PerspexModpackPatcher'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($file in @('manifest.json', 'README.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage -Force
}
Copy-Item -LiteralPath (Join-Path $root 'bin\Release\netstandard2.1\PerspexModpackPatcher.dll') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root 'icon.png') -Destination $stage -Force
$patchers = Join-Path $stage 'patchers'
New-Item -ItemType Directory -Path $patchers -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'preloader\bin\Release\netstandard2.1\PerspexLegendsPreloader.dll') -Destination $patchers -Force

$manifest = Get-Content -LiteralPath (Join-Path $stage 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'PerspexModpackPatcher' -or $manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid manifest' }
$iconBytes = [IO.File]::ReadAllBytes((Join-Path $stage 'icon.png'))
$width = [BitConverter]::ToInt32([byte[]]@($iconBytes[19], $iconBytes[18], $iconBytes[17], $iconBytes[16]), 0)
$height = [BitConverter]::ToInt32([byte[]]@($iconBytes[23], $iconBytes[22], $iconBytes[21], $iconBytes[20]), 0)
if ($width -ne 256 -or $height -ne 256) { throw 'Icon must be 256x256' }

$archive = Join-Path $root "release\PerspexModpackPatcher-$($manifest.version_number).zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
Write-Output $archive
