$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$work=Join-Path $root 'work'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$renderer=Join-Path $work 'RenderPreview.exe'
$references=@('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $framework $_) }
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path $framework ('WPF\'+$_)) }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$renderer) $references (Join-Path $PSScriptRoot 'RenderPreview.cs')
if($LASTEXITCODE -ne 0){throw 'Preview renderer build failed.'}
& $renderer (Join-Path $root 'app\CodexQuotaFloat.exe') (Join-Path $root 'docs')
if($LASTEXITCODE -ne 0){throw 'Preview render failed.'}
