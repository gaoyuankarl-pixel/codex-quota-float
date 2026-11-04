$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'app\CodexQuotaFloat.exe'
$linkPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'CodexQuotaFloat.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
if ($link.Arguments.Length -ge 1023) { throw 'Startup shortcut arguments may be truncated.' }
$script = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($link.Arguments.Split(' ')[-1]))
$testRoot = Join-Path $projectRoot ('work\startup-readiness-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$source = Join-Path $testRoot 'Probe.cs'
$template = Join-Path $testRoot 'ProbeTemplate.exe'
$delayedExe = Join-Path $testRoot 'ReadyProbe.exe'
$marker = Join-Path $testRoot 'ready.txt'
Set-Content -LiteralPath $source -Encoding utf8 -Value 'class Probe { static void Main() { System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,"ready.txt"),"started"); } }'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe ('/out:' + $template) $source
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$originalLiteral = "'" + $exe.Replace("'","''") + "'"
$testLiteral = "'" + $delayedExe.Replace("'","''") + "'"
if (-not $script.Contains($originalLiteral)) { throw 'Unexpected startup command.' }
$script = $script.Replace($originalLiteral,$testLiteral)
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
$bootstrap = Start-Process -FilePath $link.TargetPath -ArgumentList ('-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ' + $encoded) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 1
if (Test-Path -LiteralPath $marker) { throw 'Probe ran before executable became available.' }
Copy-Item -LiteralPath $template -Destination $delayedExe
if (-not $bootstrap.WaitForExit(15000)) { $bootstrap.Kill(); throw 'Startup did not complete after the target became available.' }
if (-not (Test-Path -LiteralPath $marker)) { throw 'Startup did not wait and retry.' }
Write-Output 'PASS: the installed startup command waited for a missing executable and started it after it became available. No real quota or reset operation was invoked.'
