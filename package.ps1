$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$stage = Join-Path $projectRoot ('work\package-' + [Guid]::NewGuid().ToString('N'))
$releaseDir = Join-Path $projectRoot 'releases'
New-Item -ItemType Directory -Path (Join-Path $stage 'app'),$releaseDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'app\CodexQuotaFloat.exe'),(Join-Path $projectRoot 'app\CodexQuotaFloat.exe.config') -Destination (Join-Path $stage 'app') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'README.zh-CN.md'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'Install.ps1'),(Join-Path $projectRoot 'Uninstall.ps1'),(Join-Path $projectRoot 'CHANGELOG.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $stage -Recurse
$source = Get-Content -LiteralPath (Join-Path $projectRoot 'src\Program.cs') -Raw
$version = [regex]::Match($source,'public const string Version="([0-9.]+)"').Groups[1].Value
if (-not $version) { throw 'Application version not found.' }
$zip = Join-Path $releaseDir ('CodexQuotaFloat-v' + $version + '-win-x64.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding utf8
Get-Item -LiteralPath $zip | Select-Object FullName,Length
