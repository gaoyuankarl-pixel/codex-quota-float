$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'app\CodexQuotaFloat.exe'
$stop = Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -PassThru
[void]$stop.WaitForExit(5000)
foreach ($p in (Get-Process CodexQuotaFloat -ErrorAction SilentlyContinue)) { [void]$p.WaitForExit(6000) }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$probe = Join-Path $projectRoot 'work\LaunchIsolationProbe.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $probe) (Join-Path $PSScriptRoot 'LaunchIsolationProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
& $probe $exe
if ($LASTEXITCODE -ne 0) { throw 'Launcher isolation regression failed' }
Write-Output 'The watcher and widget are left running. No real Codex process was closed.'
