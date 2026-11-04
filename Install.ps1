$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app\CodexQuotaFloat.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the application first.' }
$installer = Start-Process -FilePath $exe -ArgumentList '--install' -WindowStyle Hidden -PassThru
if (-not $installer.WaitForExit(10000)) { throw 'Installation command timed out.' }
if ($installer.ExitCode -ne 0) { throw 'Installation failed. See app\data\last-error.txt.' }
Write-Output 'Codex Quota Float is enabled in the user Run key and Startup folder, with drive-readiness retries.'
