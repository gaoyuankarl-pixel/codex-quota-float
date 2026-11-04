$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app\CodexQuotaFloat.exe'
if (Test-Path -LiteralPath $exe) {
    $uninstaller = Start-Process -FilePath $exe -ArgumentList '--uninstall' -WindowStyle Hidden -Wait -PassThru
    if ($uninstaller.ExitCode -ne 0) { throw 'Could not disable the startup entry.' }
}
Write-Output 'Startup disabled. Tool processes are exiting. Files and pending reset records are preserved.'
